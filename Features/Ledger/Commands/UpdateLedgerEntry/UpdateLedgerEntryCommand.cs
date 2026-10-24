using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.UpdateLedgerEntry
{
    public record UpdateLedgerEntryCommand(Guid UserId, Guid EntryId, UpdateLedgerEntryDto Payload)
        : IRequest<MonthlyEntryDto>;

    public class UpdateLedgerEntryCommandHandler : IRequestHandler<UpdateLedgerEntryCommand, MonthlyEntryDto>
    {
        private const string MovedNotePrefix = "Movido a ";

        private readonly IAppDbContext _db;
        private readonly IClock _clock;
        private readonly PeriodProvisioner _provisioner;

        public UpdateLedgerEntryCommandHandler(IAppDbContext db, IClock clock, PeriodProvisioner provisioner)
        {
            _db = db;
            _clock = clock;
            _provisioner = provisioner;
        }

        public async Task<MonthlyEntryDto> Handle(UpdateLedgerEntryCommand request, CancellationToken ct)
        {
            var entry = await LedgerGuards.LoadEditableEntryAsync(_db, request.UserId, request.EntryId, ct);
            var dto = request.Payload;

            if (dto.ConceptId.HasValue)
            {
                var concept = await LedgerGuards.EnsureConceptBelongsToUserAsync(_db, request.UserId, dto.ConceptId.Value, ct);
                entry.ConceptId = concept.Id;
                // Un traspaso entre cuentas propias no es ingreso ni gasto: lo decide el concepto.
                entry.IsTransfer = concept.Kind == ConceptKind.Transfer;
            }

            if (dto.AccountId.HasValue)
            {
                await LedgerGuards.EnsureAccountBelongsToUserAsync(_db, request.UserId, dto.AccountId.Value, ct);
                entry.AccountId = dto.AccountId.Value;
            }

            if (dto.ForecastAmount.HasValue) entry.ForecastAmount = dto.ForecastAmount.Value;
            if (dto.Description is not null) entry.Description = dto.Description;
            if (dto.Notes is not null) entry.Notes = dto.Notes;

            if (dto.DueDate.HasValue && dto.DueDate.Value != entry.DueDate)
            {
                await MoveToDueDateAsync(entry.UserId, entry, dto.DueDate.Value, ct);
            }

            entry.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }

        /// <summary>
        /// Cambiar la fecha puede sacar el asiento de su mes: en ese caso hay
        /// que reasignarlo al periodo que le toca, que además debe estar abierto.
        /// </summary>
        private async Task MoveToDueDateAsync(
            Guid userId, LedgerEntry entry, DateOnly newDueDate, CancellationToken ct)
        {
            var current = entry.Period;
            var changesMonth = current is null || current.Year != newDueDate.Year || current.Month != newDueDate.Month;

            if (changesMonth)
            {
                var target = await _provisioner.GetOrOpenAsync(userId, newDueDate.Year, newDueDate.Month, ct);

                if (target.Status == PeriodStatus.Closed)
                    throw new BusinessRuleException(
                        $"El periodo {target.Year}-{target.Month:D2} está cerrado: no se puede mover el asiento ahí.");

                if (entry.RecurringRuleId is Guid ruleId && current is not null)
                    await KeepRuleOccurrenceAsync(entry, ruleId, current, target, ct);

                entry.PeriodId = target.Id;
                entry.Period = target;
            }

            entry.DueDate = newDueDate;

            // Una previsión que se mueve al pasado pasa a estar vencida, y al
            // revés. Lo ya confirmado no se toca.
            if (entry.Status is EntryStatus.Planned or EntryStatus.Pending)
                entry.Status = newDueDate < _clock.Today ? EntryStatus.Pending : EntryStatus.Planned;
        }

        /// <summary>
        /// Una ocurrencia de regla que sale de su mes deja allí una línea
        /// descartada: sin ella, el mes de origen volvería a generarla y la
        /// previsión la contaría dos veces. Si vuelve a un mes del que salió,
        /// esa línea ya sobra.
        /// </summary>
        private async Task KeepRuleOccurrenceAsync(
            LedgerEntry entry, Guid ruleId, MonthlyPeriod source, MonthlyPeriod target, CancellationToken ct)
        {
            var placeholder = await _db.LedgerEntries.FirstOrDefaultAsync(other =>
                other.UserId == entry.UserId &&
                other.PeriodId == target.Id &&
                other.RecurringRuleId == ruleId &&
                other.Status == EntryStatus.Skipped &&
                other.Notes != null &&
                other.Notes.StartsWith(MovedNotePrefix), ct);
            if (placeholder is not null) _db.LedgerEntries.Remove(placeholder);

            var sourceKeepsOccurrence = await _db.LedgerEntries.AnyAsync(other =>
                other.UserId == entry.UserId &&
                other.PeriodId == source.Id &&
                other.RecurringRuleId == ruleId &&
                other.Id != entry.Id, ct);
            if (sourceKeepsOccurrence) return;

            var rule = await _db.RecurringRules.FirstOrDefaultAsync(item => item.Id == ruleId && item.UserId == entry.UserId, ct);
            if (rule is null || !RecurrenceCalculator.OccursIn(rule, source.Year, source.Month)) return;

            _db.LedgerEntries.Add(new LedgerEntry
            {
                UserId = entry.UserId,
                PeriodId = source.Id,
                ConceptId = rule.ConceptId,
                AccountId = rule.AccountId,
                RecurringRuleId = rule.Id,
                Direction = rule.Direction,
                Status = EntryStatus.Skipped,
                DueDate = entry.DueDate.Year == source.Year && entry.DueDate.Month == source.Month
                    ? entry.DueDate
                    : RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, source.Year, source.Month),
                ForecastAmount = rule.ForecastAmount,
                Description = rule.Description,
                Notes = $"{MovedNotePrefix}{target.Month:D2}/{target.Year}."
            });
        }
    }
}
