using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Comprobaciones que todo comando sobre asientos debe pasar: que el
    /// recurso sea del usuario y que su mes siga abierto.
    /// </summary>
    public static class LedgerGuards
    {
        /// <summary>
        /// Carga el asiento del usuario. Si no existe o es de otro, responde
        /// lo mismo (no encontrado) para no revelar qué identificadores existen.
        /// </summary>
        public static async Task<LedgerEntry> LoadEditableEntryAsync(
            IAppDbContext db, Guid userId, Guid entryId, CancellationToken ct, bool allowSettlementAllocation = false)
        {
            var entry = await db.LedgerEntries
                .Include(e => e.Period)
                .Include(e => e.Account)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, ct)
                ?? throw new NotFoundException("El asiento no existe.");

            if (entry.Period is not null && entry.Period.Status == PeriodStatus.Closed)
                throw new BusinessRuleException(
                    $"El periodo {entry.Period.Year}-{entry.Period.Month:D2} está cerrado. Reábrelo para modificarlo.");

            if (!allowSettlementAllocation && entry.SettlementAllocatedAmount > 0m)
                throw new BusinessRuleException(
                    "Este movimiento está incluido en una liquidación. Quita su parte de allí antes de modificarlo.");
            if (await db.SettlementLines.AnyAsync(line =>
                line.Settlement!.UserId == userId && line.LedgerEntryId == entryId
                && (line.Kind == SettlementLineKind.Payment || line.Kind == SettlementLineKind.PaymentSent), ct))
                throw new BusinessRuleException(
                    "Este movimiento paga una liquidación. Desvincúlalo allí antes de modificarlo.");

            return entry;
        }

        public static async Task<Concept> EnsureConceptBelongsToUserAsync(
            IAppDbContext db, Guid userId, Guid conceptId, CancellationToken ct)
        {
            return await db.Concepts.FirstOrDefaultAsync(c => c.Id == conceptId && c.UserId == userId, ct)
                ?? throw new BusinessRuleException("El concepto no existe o no pertenece al usuario.");
        }

        public static async Task EnsureAccountBelongsToUserAsync(
            IAppDbContext db, Guid userId, Guid? accountId, CancellationToken ct)
        {
            if (accountId is null) return;

            var exists = await db.Accounts.AnyAsync(a => a.Id == accountId.Value && a.UserId == userId, ct);

            if (!exists)
                throw new BusinessRuleException("La cuenta no existe o no pertenece al usuario.");
        }
    }
}
