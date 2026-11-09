using System.Data;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Zeus
{
    public sealed class ZeusService(AppDbContext db, IClock clock, ISender sender)
    {
        private sealed record HourState(
            List<ZeusHourPass> Passes, List<ZeusHourUsage> Usages, List<ZeusCareStay> CareStays);

        public async Task<ZeusSummaryDto> GetAsync(Guid userId, CancellationToken ct)
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, ct);
            var summary = Summarize(await LoadAsync(userId, ct));
            await transaction.CommitAsync(ct);
            return summary;
        }

        public async Task<IReadOnlyList<ZeusExpenseDto>> ExpensesAsync(
            Guid userId, int year, int month, string? provider, CancellationToken ct)
        {
            if (provider is not (null or "daycare" or "alessia"))
                throw new BusinessRuleException("Elige la guardería o Alessia.");
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
                    && !db.ZeusHourPasses.Any(pass => pass.LedgerEntryId == entry.Id)
                    && !db.ZeusCareStays.Any(stay => stay.LedgerEntryId == entry.Id
                        && (provider == null || stay.Provider != provider))
                    && (db.ZeusCareStays.Where(stay => stay.LedgerEntryId == entry.Id)
                        .Sum(stay => (decimal?)stay.Amount) ?? 0m) < entry.ActualAmount)
                .OrderByDescending(entry => entry.ValueDate ?? entry.DueDate)
                .ToListAsync(ct);
            var allocations = await db.ZeusCareStays.AsNoTracking()
                .Where(stay => stay.UserId == userId && stay.LedgerEntryId.HasValue)
                .GroupBy(stay => stay.LedgerEntryId!.Value)
                .Select(group => new { Id = group.Key, Amount = group.Sum(stay => stay.Amount) })
                .ToDictionaryAsync(item => item.Id, item => item.Amount, ct);
            return entries.Select(entry => MapExpense(entry, allocations.GetValueOrDefault(entry.Id))).ToList();
        }

        public Task<ZeusSummaryDto> BuyAsync(Guid userId, BuyZeusPassDto dto, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                if (dto.Id == Guid.Empty || state.Passes.Any(pass => pass.Id == dto.Id))
                    throw new BusinessRuleException("El bono ya existe o su identificador no es válido. Actualiza la página.");
                if (dto.Minutes < 1 || dto.Minutes > 6_000_000)
                    throw new BusinessRuleException("Indica una cantidad válida de horas.");
                var expense = await ResolveExpenseAsync(userId, dto.LedgerEntryId, dto.NewExpense,
                    $"Bono de Zeus · {dto.Minutes / 60m:0.##} h", ct);
                if (state.Passes.Any(pass => pass.LedgerEntryId == expense.Id))
                    throw new BusinessRuleException("Ese gasto ya está vinculado a un bono de Zeus.");
                if (state.CareStays.Any(stay => stay.LedgerEntryId == expense.Id))
                    throw new BusinessRuleException("Ese gasto ya paga cuidados de Zeus. No se puede usar también para un bono.");
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
                var current = Summarize(state).Passes.First(item => item.Id == id);
                if (current.UsedMinutes + current.PlannedMinutes > 0)
                    throw new BusinessRuleException("Este bono ya tiene horas usadas. Corrige primero los registros de uso.");
                state.Passes.Remove(pass);
                db.ZeusHourPasses.Remove(pass);
                return Task.CompletedTask;
            }, ct);

        public Task<ZeusSummaryDto> SaveUsageAsync(
            Guid userId, Guid? id, SaveZeusUsageDto dto, CancellationToken ct) =>
            MutateAsync(userId, state =>
            {
                ValidateDate(dto.Date, allowFuture: true);
                var minutes = ResolveUsageMinutes(dto);
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
                usage.Minutes = minutes;
                usage.StartTime = dto.StartTime;
                usage.EndTime = dto.EndTime;
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

        public Task<ZeusSummaryDto> SaveCareAsync(
            Guid userId, Guid? id, SaveZeusCareStayDto dto, CancellationToken ct) =>
            MutateAsync(userId, state =>
            {
                ValidateDate(dto.Date, allowFuture: true);
                if (dto.Provider is not ("daycare" or "alessia")
                    || dto.Kind is not ("day" or "night" or "cancellation")
                    || (dto.Provider == "daycare" && dto.Kind != "night"))
                    throw new BusinessRuleException("Elige noches de guardería o cuidados o cancelaciones de Alessia.");
                if (dto.Amount <= 0m || dto.Amount > 999_999_999m
                    || decimal.Round(dto.Amount, 2) != dto.Amount)
                    throw new BusinessRuleException("Indica un precio positivo con un máximo de dos decimales.");
                if (dto.Note?.Length > 300)
                    throw new BusinessRuleException("La nota no puede superar 300 caracteres.");
                if (dto.Kind == "day")
                {
                    if (dto.Nights != 0)
                        throw new BusinessRuleException("Un cuidado por horas no puede incluir noches.");
                    ResolveUsageMinutes(new SaveZeusUsageDto { StartTime = dto.StartTime, EndTime = dto.EndTime });
                }
                else if (dto.Kind == "night")
                {
                    if (dto.Nights < 1 || dto.Nights > 365 || dto.StartTime.HasValue || dto.EndTime.HasValue)
                        throw new BusinessRuleException("Indica entre 1 y 365 noches, sin mezclar un horario diurno.");
                    if (dto.Date.DayNumber + dto.Nights > new DateOnly(9999, 1, 1).DayNumber)
                        throw new BusinessRuleException("La estancia debe terminar antes del año 9999.");
                    ValidateDate(dto.Date.AddDays(dto.Nights - 1), allowFuture: true);
                }
                else if (dto.Nights != 0 || dto.StartTime.HasValue || dto.EndTime.HasValue)
                    throw new BusinessRuleException("Una cancelación no puede incluir horas ni noches de cuidado.");
                ZeusCareStay stay;
                if (id.HasValue)
                {
                    stay = state.CareStays.FirstOrDefault(stay => stay.Id == id)
                        ?? throw new NotFoundException("No encuentro esa estancia.");
                    if (stay.LedgerEntryId.HasValue)
                        throw new BusinessRuleException("Desvincula primero el pago o la previsión para corregir este registro. Una previsión se ajustará; un pago real se conservará.");
                }
                else
                {
                    if (dto.Id == Guid.Empty || state.CareStays.Any(stay => stay.Id == dto.Id))
                        throw new BusinessRuleException("La estancia ya existe o no es válida. Actualiza la página.");
                    stay = new ZeusCareStay { Id = dto.Id, UserId = userId, CreatedAt = clock.UtcNow };
                    state.CareStays.Add(stay);
                    db.ZeusCareStays.Add(stay);
                }
                stay.Provider = dto.Provider;
                stay.Kind = dto.Kind;
                stay.Date = dto.Date;
                stay.StartTime = dto.StartTime;
                stay.EndTime = dto.EndTime;
                stay.Nights = dto.Nights;
                stay.Amount = dto.Amount;
                stay.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();
                return Task.CompletedTask;
            }, ct);

        public Task<ZeusSummaryDto> RemoveCareAsync(Guid userId, Guid id, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                var stay = state.CareStays.FirstOrDefault(stay => stay.Id == id)
                    ?? throw new NotFoundException("No encuentro esa estancia.");
                await ReleaseForecastsAsync(state, [stay], ct);
                state.CareStays.Remove(stay);
                db.ZeusCareStays.Remove(stay);
            }, ct);

        public Task<ZeusSummaryDto> UnlinkCarePaymentAsync(Guid userId, Guid id, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                var stay = state.CareStays.FirstOrDefault(stay => stay.Id == id)
                    ?? throw new NotFoundException("No encuentro esa estancia.");
                if (!stay.LedgerEntryId.HasValue)
                    throw new BusinessRuleException("Esta estancia no tiene un pago vinculado.");
                await ReleaseForecastsAsync(state, [stay], ct);
                stay.LedgerEntryId = null;
                stay.LedgerEntry = null;
            }, ct);

        public Task<ZeusSummaryDto> PayCareAsync(Guid userId, PayZeusCareDto dto, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                if (dto.StayIds.Count == 0 || dto.StayIds.Count != dto.StayIds.Distinct().Count())
                    throw new BusinessRuleException("Selecciona una o varias estancias distintas para este pago.");
                var stays = state.CareStays.Where(stay => dto.StayIds.Contains(stay.Id)).ToList();
                if (stays.Count != dto.StayIds.Count || stays.Any(stay => stay.LedgerEntry?.Status == EntryStatus.Paid))
                    throw new BusinessRuleException("Alguna estancia no existe o ya está pagada. Actualiza la página.");
                if (stays.Select(stay => stay.Provider).Distinct().Count() != 1)
                    throw new BusinessRuleException("Un pago debe corresponder a la guardería o a Alessia, no a ambas.");
                var provider = stays[0].Provider;
                var forecast = dto.NewForecast;
                if (forecast is not null && (dto.LedgerEntryId.HasValue || dto.NewExpense is not null))
                    throw new BusinessRuleException("Elige una previsión nueva o un pago, no ambos.");
                if (forecast is not null && forecast.Amount != stays.Sum(stay => stay.Amount))
                    throw new BusinessRuleException("La previsión debe coincidir con el total de las estancias seleccionadas.");
                var expense = await ResolveExpenseAsync(userId, dto.LedgerEntryId, forecast ?? dto.NewExpense,
                    $"Cuidados de Zeus · {(provider == "alessia" ? "Alessia" : "Guardería")} · {stays.Count} estancia(s)",
                    ct, isForecast: forecast is not null);
                if (state.Passes.Any(pass => pass.LedgerEntryId == expense.Id))
                    throw new BusinessRuleException("Ese gasto ya compra un bono de horas de Zeus.");
                var linked = state.CareStays.Where(stay => stay.LedgerEntryId == expense.Id).ToList();
                if (linked.Any(stay => stay.Provider != provider))
                    throw new BusinessRuleException("Ese gasto ya paga al otro cuidador de Zeus.");
                if (linked.Sum(stay => stay.Amount) + stays.Sum(stay => stay.Amount) > expense.EffectiveAmount)
                    throw new BusinessRuleException("El importe disponible del gasto no cubre las estancias seleccionadas. Revisa la selección o el precio de cada estancia.");
                await ReleaseForecastsAsync(state, stays, ct);
                foreach (var stay in stays)
                {
                    stay.LedgerEntryId = expense.Id;
                    stay.LedgerEntry = expense;
                }
            }, ct);

        public Task<ZeusSummaryDto> ConfirmCarePaymentAsync(
            Guid userId, Guid entryId, ConfirmLedgerEntryDto dto, CancellationToken ct) =>
            MutateAsync(userId, async state =>
            {
                var stays = state.CareStays.Where(stay => stay.LedgerEntryId == entryId).ToList();
                if (stays.Count == 0 || stays[0].LedgerEntry?.Status is not (EntryStatus.Planned or EntryStatus.Pending))
                    throw new BusinessRuleException("No encuentro una previsión pendiente para estos cuidados.");
                await sender.Send(new ConfirmLedgerEntryCommand(userId, entryId, dto), ct);
            }, ct);

        private async Task ReleaseForecastsAsync(HourState state, IReadOnlyList<ZeusCareStay> removed, CancellationToken ct)
        {
            foreach (var group in removed.Where(stay => stay.LedgerEntryId.HasValue)
                         .GroupBy(stay => stay.LedgerEntryId!.Value))
            {
                var entry = group.First().LedgerEntry!;
                if (entry.Status is not (EntryStatus.Planned or EntryStatus.Pending)) continue;
                await LedgerGuards.LoadEditableEntryAsync(db, entry.UserId, entry.Id, ct, allowZeusCareForecast: true);
                if (entry.ImportRowId.HasValue || entry.RecurringRuleId.HasValue)
                    throw new BusinessRuleException("Esta previsión no es exclusiva de los cuidados de Zeus. Desvincúlala antes de modificar su origen.");
                var remaining = state.CareStays.Where(stay => stay.LedgerEntryId == entry.Id && !removed.Contains(stay))
                    .Sum(stay => stay.Amount);
                if (remaining == 0m)
                {
                    entry.Status = EntryStatus.Skipped;
                    entry.ActualAmount = null;
                    entry.ValueDate = null;
                }
                else entry.ForecastAmount = remaining;
                entry.UpdatedAt = clock.UtcNow;
            }
        }

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
            await db.ZeusHourUsages.Where(usage => usage.UserId == userId).ToListAsync(ct),
            await db.ZeusCareStays.Include(stay => stay.LedgerEntry).ThenInclude(entry => entry!.Concept)
                .Include(stay => stay.LedgerEntry).ThenInclude(entry => entry!.Account)
                .Where(stay => stay.UserId == userId).ToListAsync(ct));

        private ZeusSummaryDto Summarize(HourState state)
        {
            foreach (var group in state.CareStays.Where(stay => stay.Kind != "cancellation")
                         .GroupBy(stay => new { stay.Provider, stay.Kind }))
            {
                DateTime? previousEnd = null;
                foreach (var stay in group.OrderBy(stay => stay.Date).ThenBy(stay => stay.StartTime))
                {
                    var start = stay.Date.ToDateTime(stay.StartTime ?? TimeOnly.MinValue);
                    var end = stay.Kind == "night" ? stay.Date.AddDays(stay.Nights).ToDateTime(TimeOnly.MinValue)
                        : stay.Date.ToDateTime(stay.EndTime!.Value);
                    if (previousEnd.HasValue && start < previousEnd.Value)
                        throw new BusinessRuleException("Esta estancia se solapa con otra del mismo cuidador. Corrige el registro existente.");
                    previousEnd = end;
                }
            }
            if (state.Usages.GroupBy(usage => usage.Date).Any(day => day.Sum(usage => (long)usage.Minutes) > 1440))
                throw new BusinessRuleException("La suma de los registros de un día no puede superar 24 horas.");
            foreach (var day in state.Usages.Where(usage => usage.StartTime.HasValue && usage.EndTime.HasValue)
                         .GroupBy(usage => usage.Date))
            {
                TimeOnly? previousEnd = null;
                foreach (var usage in day.OrderBy(usage => usage.StartTime))
                {
                    if (previousEnd.HasValue && usage.StartTime!.Value < previousEnd.Value)
                        throw new BusinessRuleException(
                            $"El horario se solapa con otro registro del {usage.Date:dd/MM/yyyy}. Corrige el registro existente para no descontar horas dos veces.");
                    previousEnd = usage.EndTime;
                }
            }
            var passes = state.Passes.OrderBy(pass => pass.PurchasedOn)
                .ThenBy(pass => pass.CreatedAt).ThenBy(pass => pass.Id).ToList();
            var remaining = passes.ToDictionary(pass => pass.Id, pass => pass.Minutes);
            var plannedByPass = passes.ToDictionary(pass => pass.Id, _ => 0);
            var usages = new List<ZeusUsageDto>();
            foreach (var usage in state.Usages.OrderBy(usage => usage.Date)
                         .ThenBy(usage => usage.StartTime ?? TimeOnly.MinValue)
                         .ThenBy(usage => usage.CreatedAt).ThenBy(usage => usage.Id))
            {
                var needed = usage.Minutes;
                var isPlanned = usage.Date > clock.Today;
                var allocations = new List<ZeusUsageAllocationDto>();
                foreach (var pass in passes.Where(pass => pass.PurchasedOn <= usage.Date))
                {
                    var amount = Math.Min(needed, remaining[pass.Id]);
                    if (amount <= 0) continue;
                    remaining[pass.Id] -= amount;
                    if (isPlanned) plannedByPass[pass.Id] += amount;
                    needed -= amount;
                    allocations.Add(new ZeusUsageAllocationDto(pass.Id, pass.PurchasedOn, amount));
                    if (needed == 0) break;
                }
                if (needed > 0)
                    throw new BusinessRuleException(
                        $"No hay horas suficientes para el {usage.Date:dd/MM/yyyy}. Compra un bono anterior a esa fecha o corrige el uso.");
                usages.Add(new ZeusUsageDto(
                    usage.Id, usage.Date, usage.Minutes, usage.Note, allocations, usage.StartTime, usage.EndTime, isPlanned));
            }
            var passDtos = passes.Select(pass => new ZeusPassDto(
                pass.Id, pass.PurchasedOn, pass.Minutes,
                pass.Minutes - remaining[pass.Id] - plannedByPass[pass.Id],
                remaining[pass.Id] + plannedByPass[pass.Id], MapExpense(pass.LedgerEntry!),
                plannedByPass[pass.Id])).Reverse().ToList();
            var plannedMinutes = usages.Where(usage => usage.IsPlanned).Sum(usage => (long)usage.Minutes);
            return new ZeusSummaryDto(
                passes.Sum(pass => (long)pass.Minutes),
                usages.Sum(usage => (long)usage.Minutes) - plannedMinutes,
                remaining.Values.Sum(value => (long)value) + plannedMinutes,
                passDtos.Sum(pass => pass.Expense.Amount),
                passDtos, usages.AsEnumerable().Reverse().ToList(),
                state.CareStays.OrderByDescending(stay => stay.Date).ThenByDescending(stay => stay.StartTime)
                    .ThenByDescending(stay => stay.CreatedAt)
                    .Select(stay => new ZeusCareStayDto(stay.Id, stay.Provider, stay.Kind, stay.Date,
                        stay.StartTime, stay.EndTime, stay.Nights, stay.Amount, stay.Note,
                        stay.LedgerEntry is null ? null : MapExpense(stay.LedgerEntry,
                            state.CareStays.Where(other => other.LedgerEntryId == stay.LedgerEntryId)
                                .Sum(other => other.Amount)))).ToList(),
                plannedMinutes);
        }

        private static ZeusExpenseDto MapExpense(LedgerEntry entry, decimal allocatedAmount = 0m) => new(
            entry.Id, entry.ValueDate ?? entry.DueDate, entry.Description ?? entry.Concept!.Name,
            entry.Concept!.Name, entry.Account?.Name ?? "Sin cuenta",
            entry.Status == EntryStatus.Paid ? entry.ActualAmount!.Value : entry.ForecastAmount, allocatedAmount, entry.Status);

        private async Task<LedgerEntry> ResolveExpenseAsync(
            Guid userId, Guid? ledgerEntryId, ZeusPurchaseExpenseDto? purchase, string description,
            CancellationToken ct, bool isForecast = false)
        {
            if (ledgerEntryId.HasValue == (purchase is not null))
                throw new BusinessRuleException("Vincula un gasto existente o crea uno nuevo, no ambos.");
            Guid entryId;
            if (purchase is not null)
            {
                ValidateDate(purchase.Date, allowFuture: isForecast);
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
                    throw new BusinessRuleException("Elige una cuenta activa con inicio contable anterior al pago.");
                var entry = await sender.Send(new CreateLedgerEntryCommand(userId, new CreateLedgerEntryDto
                {
                    ConceptId = purchase.ConceptId, AccountId = purchase.AccountId,
                    Direction = EntryDirection.Out, DueDate = purchase.Date, ValueDate = isForecast ? null : purchase.Date,
                    ForecastAmount = purchase.Amount, ActualAmount = isForecast ? null : purchase.Amount, Description = description
                }), ct);
                entryId = entry.Id;
            }
            else entryId = ledgerEntryId!.Value;
            var expense = await db.LedgerEntries.Include(entry => entry.Concept).Include(entry => entry.Account)
                .FirstOrDefaultAsync(entry => entry.Id == entryId && entry.UserId == userId, ct)
                ?? throw new NotFoundException("No encuentro ese gasto.");
            if ((isForecast ? expense.Status is not (EntryStatus.Planned or EntryStatus.Pending)
                    : expense.Status != EntryStatus.Paid)
                || expense.Direction != EntryDirection.Out
                || expense.IsTransfer || expense.Concept?.Kind != ConceptKind.Expense
                || (!isForecast && expense.ActualAmount is null or <= 0m)
                || expense.EffectiveAmount <= 0m || expense.AccountId is null
                || expense.SettlementAllocatedAmount != 0m)
                throw new BusinessRuleException("Elige un gasto pagado, sin traspasos ni importes compensados.");
            ValidateDate(expense.ValueDate ?? expense.DueDate, allowFuture: isForecast);
            return expense;
        }

        private static int ResolveUsageMinutes(SaveZeusUsageDto dto)
        {
            if (dto.StartTime.HasValue || dto.EndTime.HasValue)
            {
                if (dto.StartTime is not { } start || dto.EndTime is not { } end)
                    throw new BusinessRuleException("Indica la hora de entrada y la de salida.");
                if (start.Ticks % TimeSpan.TicksPerMinute != 0 || end.Ticks % TimeSpan.TicksPerMinute != 0)
                    throw new BusinessRuleException("Indica el horario en horas y minutos, sin segundos.");
                if (end <= start)
                    throw new BusinessRuleException("La salida debe ser posterior a la entrada en el mismo día.");
                var minutes = (int)(end - start).TotalMinutes;
                if (dto.Minutes.HasValue && dto.Minutes.Value != minutes)
                    throw new BusinessRuleException("La duración no coincide con el horario de entrada y salida.");
                return minutes;
            }
            if (dto.Minutes is not { } legacyMinutes || legacyMinutes < 1 || legacyMinutes > 1440)
                throw new BusinessRuleException("Indica la hora de entrada y la de salida.");
            return legacyMinutes;
        }

        private void ValidateDate(DateOnly date, bool allowFuture = false)
        {
            if (date.Year < 2000 || date.Year > 9998 || (!allowFuture && date > clock.Today))
                throw new BusinessRuleException(allowFuture
                    ? "Elige una fecha válida entre los años 2000 y 9998."
                    : "Elige una fecha de hoy o anterior, desde el año 2000.");
        }
    }
}
