using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

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
        /// lo mismo (no encontrado) para no revelar quÃ© identificadores existen.
        /// </summary>
        public static async Task<LedgerEntry> LoadEditableEntryAsync(
            IAppDbContext db, Guid userId, Guid entryId, CancellationToken ct)
        {
            var entry = await db.LedgerEntries
                .Include(e => e.Period)
                .Include(e => e.Account)
                .FirstOrDefaultAsync(e => e.Id == entryId && e.UserId == userId, ct)
                ?? throw new NotFoundException("El asiento no existe.");

            if (entry.Period is not null && entry.Period.Status == PeriodStatus.Closed)
                throw new BusinessRuleException(
                    $"El periodo {entry.Period.Year}-{entry.Period.Month:D2} estÃ¡ cerrado. ReÃ¡brelo para modificarlo.");

            return entry;
        }

        public static async Task EnsureConceptBelongsToUserAsync(
            IAppDbContext db, Guid userId, Guid conceptId, CancellationToken ct)
        {
            var exists = await db.Concepts.AnyAsync(c => c.Id == conceptId && c.UserId == userId, ct);

            if (!exists)
                throw new BusinessRuleException("El concepto no existe o no pertenece al usuario.");
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
