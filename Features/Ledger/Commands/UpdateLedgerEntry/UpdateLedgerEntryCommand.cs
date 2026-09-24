using MediatR;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.UpdateLedgerEntry
{
    public record UpdateLedgerEntryCommand(Guid UserId, Guid EntryId, UpdateLedgerEntryDto Payload)
        : IRequest<MonthlyEntryDto>;

    public class UpdateLedgerEntryCommandHandler : IRequestHandler<UpdateLedgerEntryCommand, MonthlyEntryDto>
    {
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
                await LedgerGuards.EnsureConceptBelongsToUserAsync(_db, request.UserId, dto.ConceptId.Value, ct);
                entry.ConceptId = dto.ConceptId.Value;
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
        /// que reasignarlo al periodo que le toca, que ademÃ¡s debe estar abierto.
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
                        $"El periodo {target.Year}-{target.Month:D2} estÃ¡ cerrado: no se puede mover el asiento ahÃ­.");

                entry.PeriodId = target.Id;
                entry.Period = target;
            }

            entry.DueDate = newDueDate;

            // Una previsiÃ³n que se mueve al pasado pasa a estar vencida, y al
            // revÃ©s. Lo ya confirmado no se toca.
            if (entry.Status is EntryStatus.Planned or EntryStatus.Pending)
                entry.Status = newDueDate < _clock.Today ? EntryStatus.Pending : EntryStatus.Planned;
        }
    }
}
