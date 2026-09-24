using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry
{
    public record CreateLedgerEntryCommand(Guid UserId, CreateLedgerEntryDto Payload) : IRequest<MonthlyEntryDto>;

    public class CreateLedgerEntryCommandHandler : IRequestHandler<CreateLedgerEntryCommand, MonthlyEntryDto>
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;
        private readonly PeriodProvisioner _provisioner;

        public CreateLedgerEntryCommandHandler(IAppDbContext db, IClock clock, PeriodProvisioner provisioner)
        {
            _db = db;
            _clock = clock;
            _provisioner = provisioner;
        }

        public async Task<MonthlyEntryDto> Handle(CreateLedgerEntryCommand request, CancellationToken ct)
        {
            var dto = request.Payload;

            await LedgerGuards.EnsureConceptBelongsToUserAsync(_db, request.UserId, dto.ConceptId, ct);
            await LedgerGuards.EnsureAccountBelongsToUserAsync(_db, request.UserId, dto.AccountId, ct);

            var period = await _provisioner.GetOrOpenAsync(
                request.UserId, dto.DueDate.Year, dto.DueDate.Month, ct);

            if (period.Status == PeriodStatus.Closed)
                throw new BusinessRuleException(
                    $"El periodo {period.Year}-{period.Month:D2} estÃ¡ cerrado. ReÃ¡brelo para aÃ±adir asientos.");

            var confirmed = dto.ActualAmount.HasValue;

            var entry = new LedgerEntry
            {
                UserId = request.UserId,
                PeriodId = period.Id,
                ConceptId = dto.ConceptId,
                AccountId = dto.AccountId,
                Direction = dto.Direction,
                DueDate = dto.DueDate,
                ForecastAmount = dto.ForecastAmount,
                ActualAmount = dto.ActualAmount,
                ValueDate = confirmed ? dto.ValueDate ?? dto.DueDate : dto.ValueDate,
                Description = dto.Description,
                Notes = dto.Notes,
                Status = confirmed
                    ? EntryStatus.Paid
                    : dto.DueDate < _clock.Today ? EntryStatus.Pending : EntryStatus.Planned
            };

            _db.LedgerEntries.Add(entry);
            await _db.SaveChangesAsync(ct);

            entry.Account = dto.AccountId is null
                ? null
                : await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == dto.AccountId.Value, ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }
    }
}
