using MediatR;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.UnconfirmLedgerEntry
{
    /// <summary>Deshace una confirmaciÃ³n: el asiento vuelve a ser una previsiÃ³n.</summary>
    public record UnconfirmLedgerEntryCommand(Guid UserId, Guid EntryId) : IRequest<MonthlyEntryDto>;

    public class UnconfirmLedgerEntryCommandHandler : IRequestHandler<UnconfirmLedgerEntryCommand, MonthlyEntryDto>
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public UnconfirmLedgerEntryCommandHandler(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<MonthlyEntryDto> Handle(UnconfirmLedgerEntryCommand request, CancellationToken ct)
        {
            var entry = await LedgerGuards.LoadEditableEntryAsync(_db, request.UserId, request.EntryId, ct);

            if (entry.Status != EntryStatus.Paid)
                throw new BusinessRuleException("El asiento no estÃ¡ confirmado.");

            entry.ActualAmount = null;
            entry.ValueDate = null;
            entry.Status = entry.DueDate < _clock.Today ? EntryStatus.Pending : EntryStatus.Planned;
            entry.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }
    }
}
