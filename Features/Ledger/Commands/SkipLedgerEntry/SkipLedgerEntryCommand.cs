using MediatR;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.SkipLedgerEntry
{
    /// <summary>
    /// Descarta un asiento sin borrarlo. Es lo que hay que usar con las lÃ­neas
    /// que nacen de una regla recurrente: borrarlas sÃ³lo harÃ­a que el sistema
    /// volviera a crearlas.
    /// </summary>
    public record SkipLedgerEntryCommand(Guid UserId, Guid EntryId, bool Skip) : IRequest<MonthlyEntryDto>;

    public class SkipLedgerEntryCommandHandler : IRequestHandler<SkipLedgerEntryCommand, MonthlyEntryDto>
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;

        public SkipLedgerEntryCommandHandler(IAppDbContext db, IClock clock)
        {
            _db = db;
            _clock = clock;
        }

        public async Task<MonthlyEntryDto> Handle(SkipLedgerEntryCommand request, CancellationToken ct)
        {
            var entry = await LedgerGuards.LoadEditableEntryAsync(_db, request.UserId, request.EntryId, ct);

            if (request.Skip)
            {
                entry.Status = EntryStatus.Skipped;
                entry.ActualAmount = null;
                entry.ValueDate = null;
            }
            else
            {
                entry.Status = entry.DueDate < _clock.Today ? EntryStatus.Pending : EntryStatus.Planned;
            }

            entry.UpdatedAt = _clock.UtcNow;

            await _db.SaveChangesAsync(ct);

            return MonthlySummaryBuilder.MapEntry(entry);
        }
    }
}
