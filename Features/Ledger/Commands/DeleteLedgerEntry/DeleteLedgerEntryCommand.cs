using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;

namespace PersonalFinance.Api.Features.Ledger.Commands.DeleteLedgerEntry
{
    public record DeleteLedgerEntryCommand(Guid UserId, Guid EntryId) : IRequest;

    public class DeleteLedgerEntryCommandHandler : IRequestHandler<DeleteLedgerEntryCommand>
    {
        private readonly IAppDbContext _db;

        public DeleteLedgerEntryCommandHandler(IAppDbContext db) => _db = db;

        public async Task Handle(DeleteLedgerEntryCommand request, CancellationToken ct)
        {
            var entry = await LedgerGuards.LoadEditableEntryAsync(_db, request.UserId, request.EntryId, ct);
            if (await _db.ImportRows.AnyAsync(row => row.UserId == request.UserId &&
                row.PlannedAllocations.Any(link => link.PlannedEntryId == entry.Id), ct))
                throw new BusinessRuleException("El previsto forma parte del reparto de un pago importado y se conserva para mantener su detalle.");

            // Borrar una línea generada por una regla no sirve de nada: la
            // siguiente consulta del mes la volvería a crear.
            if (entry.RecurringRuleId.HasValue)
                throw new BusinessRuleException(
                    "El asiento procede de una regla recurrente. Descártalo en lugar de borrarlo, " +
                    "o desactiva la regla si ya no aplica.");

            _db.LedgerEntries.Remove(entry);

            await _db.SaveChangesAsync(ct);
        }
    }
}
