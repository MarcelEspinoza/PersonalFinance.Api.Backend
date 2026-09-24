using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.ConfirmLedgerEntry
{
    public record ConfirmLedgerEntryCommand(Guid UserId, Guid EntryId, ConfirmLedgerEntryDto Payload)
        : IRequest<MonthlyEntryDto>;

    public class ConfirmLedgerEntryCommandHandler : IRequestHandler<ConfirmLedgerEntryCommand, MonthlyEntryDto>
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public ConfirmLedgerEntryCommandHandler(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<MonthlyEntryDto> Handle(ConfirmLedgerEntryCommand request, CancellationToken ct)
        {
            var entry = await LedgerGuards.LoadEditableEntryAsync(_db, request.UserId, request.EntryId, ct);

            if (entry.Status == EntryStatus.Skipped)
                throw new BusinessRuleException("El asiento estÃ¡ descartado. ReactÃ­valo antes de confirmarlo.");

            await LedgerGuards.EnsureAccountBelongsToUserAsync(_db, request.UserId, request.Payload.AccountId, ct);

            entry.ActualAmount = request.Payload.ActualAmount;
            entry.ValueDate = request.Payload.ValueDate ?? _clock.Today;
            entry.Status = EntryStatus.Paid;
            entry.UpdatedAt = _clock.UtcNow;

            if (request.Payload.AccountId.HasValue)
                entry.AccountId = request.Payload.AccountId.Value;

            await _db.SaveChangesAsync(ct);

            entry.Account = entry.AccountId is null
                ? null
                : await _db.Accounts.AsNoTracking().FirstOrDefaultAsync(a => a.Id == entry.AccountId.Value, ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }
    }
}
