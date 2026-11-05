using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Zeus
{
    public sealed class ZeusService(AppDbContext db, IClock clock, ISender sender)
    {
        private sealed record HourState(List<ZeusHourPass> Passes, List<ZeusHourUsage> Usages);

        public async Task<ZeusSummaryDto> GetAsync(Guid userId, CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var summary = Summarize(await LoadAsync(userId, ct));
            await transaction.CommitAsync(ct);
            return summary;
        }

        public async Task<IReadOnlyList<ZeusExpenseDto>> ExpensesAsync(
            Guid userId, int year, int month, CancellationToken ct)
        {
            if (year < 2000 || year > 9998 || month < 1 || month > 12)
                throw new BusinessRuleException("Elige un mes válido.");
            var start = new DateOnly(year, month, 1);
            var end = start.AddMonths(1);
            var entries = await db.LedgerEntries.AsNoTracking()
                .Include(entry => entry.Concept).Include(entry => entry.Account)
                .Where(entry => entry.UserId == userId && entry.Status == EntryStatus.Paid
                    && entry.Direction == EntryDirection.Out && !entry.IsTransfer
                    && entry.Concept!.Kind == ConceptKind.Expense
                    && entry.ActualAmount > 0m && entry.AccountId != null
                    && entry.SettlementAllocatedAmount == 0m
                    && (entry.ValueDate ?? entry.DueDate) >= start
                    && (entry.ValueDate ?? entry.DueDate) < end
                    && (entry.ValueDate ?? entry.DueDate) <= clock.Today
                    && !db.ZeusHourPasses.Any(pass => pass.LedgerEntryId == entry.Id))
                .OrderByDescending(entry => entry.ValueDate ?? entry.DueDate)
                .ToListAsync(ct);
            return entries.Select(MapExpense).ToList();
        }

        public Task<ZeusSummaryDto> BuyAsync(Guid userId, BuyZeusPassDto dto, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                if (dto.Id == Guid.Empty || state.Passes.Any(pass => pass.Id == dto.Id))
                    throw new BusinessRuleException("El bono ya existe o su identificador no es válido. Actualiza la página.");
                if (dto.Minutes < 1 || dto.Minutes > 6_000_000)
                    throw new BusinessRuleException("Indica una cantidad válida de horas.");
                if (dto.LedgerEntryId.HasValue == (dto.NewExpense is not null))
                    throw new BusinessRuleException("Vincula un gasto existente o crea uno nuevo, no ambos.");

                Guid entryId;
                if (dto.NewExpense is { } purchase)
                {
                    ValidateDate(purchase.Date);
                    if (purchase.Amount <= 0m || purchase.Amount > 999_999_999m
                        || decimal.Round(purchase.Amount, 2) != purchase.Amount)
                        throw new BusinessRuleException("El precio debe ser positivo y tener como máximo dos decimales.");
                    var concept = await db.Concepts.FirstOrDefaultAsync(concept =>
                        concept.Id == purchase.ConceptId && concept.UserId == userId
                        && concept.IsActive && concept.Kind == ConceptKind.Expense, ct);
                    if (concept is null)
                        throw new BusinessRuleException("Elige un concepto de gasto activo.");
                    var account = await db.Accounts.FirstOrDefaultAsync(account =>
                        account.Id == purchase.AccountId && account.UserId == userId && account.IsActive, ct);
                    if (account is null || account.OpeningDate > purchase.Date)
                        throw new BusinessRuleException("Elige una cuenta activa con inicio contable anterior a la compra.");
                    var entry = await sender.Send(new CreateLedgerEntryCommand(userId, new CreateLedgerEntryDto
                    {
                        ConceptId = purchase.ConceptId,
                        AccountId = purchase.AccountId,
                        Direction = EntryDirection.Out,
                        DueDate = purchase.Date,
                        ValueDate = purchase.Date,
                        ForecastAmount = purchase.Amount,
                        ActualAmount = purchase.Amount,
                        Description = $"Bono de Zeus · {dto.Minutes / 60m:0.##} h"
                    }), ct);
                    entryId = entry.Id;
                }
                else entryId = dto.LedgerEntryId!.Value;

                var expense = await db.LedgerEntries
                    .Include(entry => entry.Concept).Include(entry => entry.Account)
                    .FirstOrDefaultAsync(entry => entry.Id == entryId && entry.UserId == userId, ct)
                    ?? throw new NotFoundException("No encuentro ese gasto.");
                if (expense.Status != EntryStatus.Paid || expense.Direction != EntryDirection.Out
                    || expense.IsTransfer || expense.Concept?.Kind != ConceptKind.Expense
                    || expense.ActualAmount is null or <= 0m || expense.AccountId is null
                    || expense.SettlementAllocatedAmount != 0m)
                    throw new BusinessRuleException("El bono necesita un gasto pagado, sin traspasos ni importes compensados.");
                if (state.Passes.Any(pass => pass.LedgerEntryId == expense.Id))
                    throw new BusinessRuleException("Ese gasto ya está vinculado a un bono de Zeus.");
                var date = expense.ValueDate ?? expense.DueDate;
                ValidateDate(date);
                var pass = new ZeusHourPass
                {
                    Id = dto.Id, UserId = userId, Minutes = dto.Minutes,
                    PurchasedOn = date, LedgerEntryId = expense.Id,
                    LedgerEntry = expense, CreatedAt = clock.UtcNow
                };
                state.Passes.Add(pass);
                db.ZeusHourPasses.Add(pass);
            }, ct);

        public Task<ZeusSummaryDto> RemovePassAsync(Guid userId, Guid id, CancellationToken ct) =>
            MutateAsync(userId, state =>
            {
                var pass = state.Passes.FirstOrDefault(pass => pass.Id == id)
                    ?? throw new NotFoundException("No encuentro ese bono.");
                if (Summarize(state).Passes.First(item => item.Id == id).UsedMinutes > 0)
                    throw new BusinessRuleException("Este bono ya tiene horas usadas. Corrige primero los registros de uso.");
                state.Passes.Remove(pass);
                db.ZeusHourPasses.Remove(pass);
                return Task.CompletedTask;
            }, ct);

        public Task<ZeusSummaryDto> SaveUsageAsync(
            Guid userId, Guid? id, SaveZeusUsageDto dto, CancellationToken ct) =>
            MutateAsync(userId, state =>
            {
                ValidateDate(dto.Date);
                if (dto.Minutes < 1 || dto.Minutes > 1440)
                    throw new BusinessRuleException("Cada registro debe durar entre 1 minuto y 24 horas.");
                if (dto.Note?.Length > 300)
                    throw new BusinessRuleException("La nota no puede superar 300 caracteres.");
                ZeusHourUsage usage;
                if (id.HasValue)
                    usage = state.Usages.FirstOrDefault(usage => usage.Id == id.Value)
                        ?? throw new NotFoundException("No encuentro ese registro de horas.");
                else
                {
                    if (dto.Id == Guid.Empty || state.Usages.Any(usage => usage.Id == dto.Id))
                        throw new BusinessRuleException("El registro ya existe o no es válido. Actualiza la página.");
                    usage = new ZeusHourUsage { Id = dto.Id, UserId = userId, CreatedAt = clock.UtcNow };
                    state.Usages.Add(usage);
                    db.ZeusHourUsages.Add(usage);
                }
                usage.Date = dto.Date;
                usage.Minutes = dto.Minutes;
                usage.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
                return Task.CompletedTask;
            }, ct);

        public Task<ZeusSummaryDto> RemoveUsageAsync(Guid userId, Guid id, CancellationToken ct) =>
            MutateAsync(userId, state =>
            {
                var usage = state.Usages.FirstOrDefault(usage => usage.Id == id)
                    ?? throw new NotFoundException("No encuentro ese registro de horas.");
                state.Usages.Remove(usage);
                db.ZeusHourUsages.Remove(usage);
                return Task.CompletedTask;
            }, ct);

        private async Task<ZeusSummaryDto> MutateAsync(
            Guid userId, Func<HourState, Task> mutation, CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
            var state = await LoadAsync(userId, ct);
            await mutation(state);
            var summary = Summarize(state);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return summary;
        }

        private async Task<HourState> LoadAsync(Guid userId, CancellationToken ct) => new(
            await db.ZeusHourPasses.Include(pass => pass.LedgerEntry).ThenInclude(entry => entry!.Concept)
                .Include(pass => pass.LedgerEntry).ThenInclude(entry => entry!.Account)
                .Where(pass => pass.UserId == userId).ToListAsync(ct),
            await db.ZeusHourUsages.Where(usage => usage.UserId == userId).ToListAsync(ct));

        private static ZeusSummaryDto Summarize(HourState state)
        {
            if (state.Usages.GroupBy(usage => usage.Date).Any(day => day.Sum(usage => (long)usage.Minutes) > 1440))
                throw new BusinessRuleException("La suma de los registros de un día no puede superar 24 horas.");
            var passes = state.Passes.OrderBy(pass => pass.PurchasedOn)
                .ThenBy(pass => pass.CreatedAt).ThenBy(pass => pass.Id).ToList();
            var remaining = passes.ToDictionary(pass => pass.Id, pass => pass.Minutes);
            var usages = new List<ZeusUsageDto>();
            foreach (var usage in state.Usages.OrderBy(usage => usage.Date)
                         .ThenBy(usage => usage.CreatedAt).ThenBy(usage => usage.Id))
            {
                var needed = usage.Minutes;
                var allocations = new List<ZeusUsageAllocationDto>();
                foreach (var pass in passes.Where(pass => pass.PurchasedOn <= usage.Date))
                {
                    var amount = Math.Min(needed, remaining[pass.Id]);
                    if (amount <= 0) continue;
                    remaining[pass.Id] -= amount;
                    needed -= amount;
                    allocations.Add(new ZeusUsageAllocationDto(pass.Id, pass.PurchasedOn, amount));
                    if (needed == 0) break;
                }
                if (needed > 0)
                    throw new BusinessRuleException(
                        $"No hay horas suficientes para el {usage.Date:dd/MM/yyyy}. Compra un bono anterior a esa fecha o corrige el uso.");
                usages.Add(new ZeusUsageDto(usage.Id, usage.Date, usage.Minutes, usage.Note, allocations));
            }
            var passDtos = passes.Select(pass => new ZeusPassDto(
                pass.Id, pass.PurchasedOn, pass.Minutes, pass.Minutes - remaining[pass.Id],
                remaining[pass.Id], MapExpense(pass.LedgerEntry!))).Reverse().ToList();
            return new ZeusSummaryDto(
                passes.Sum(pass => (long)pass.Minutes),
                usages.Sum(usage => (long)usage.Minutes),
                remaining.Values.Sum(value => (long)value),
                passDtos.Sum(pass => pass.Expense.Amount),
                passDtos, usages.AsEnumerable().Reverse().ToList());
        }

        private static ZeusExpenseDto MapExpense(LedgerEntry entry) => new(
            entry.Id, entry.ValueDate ?? entry.DueDate, entry.Description ?? entry.Concept!.Name,
            entry.Concept!.Name, entry.Account?.Name ?? "Sin cuenta", entry.ActualAmount!.Value);

        private void ValidateDate(DateOnly date)
        {
            if (date.Year < 2000 || date > clock.Today)
                throw new BusinessRuleException("Elige una fecha de hoy o anterior, desde el año 2000.");
        }
    }
}
