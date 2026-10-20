using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Refleja en una cuenta sin extracto propio (por ejemplo, la hucha de
    /// Revolut) los traspasos importados en otra cuenta. El espejo es un asiento
    /// en sentido contrario con el mismo concepto, importe, fechas y fila de
    /// importación, pero sin huella: así no choca con el índice único y se
    /// confirma junto al original cuando un pendiente pasa a pagado.
    /// Sólo se reflejan movimientos importados; editar o borrar a mano el
    /// asiento original no toca su espejo.
    /// </summary>
    public static class TransferMirror
    {
        public static bool ShouldMirror(LedgerEntry source, Account? target) =>
            target is not null &&
            target.TransferConceptId is not null &&
            source.IsTransfer &&
            source.ImportRowId is not null &&
            source.AccountId is not null &&
            source.AccountId != target.Id &&
            source.ConceptId == target.TransferConceptId;

        public static LedgerEntry Create(LedgerEntry source, Guid targetAccountId) => new()
        {
            UserId = source.UserId,
            PeriodId = source.PeriodId,
            ConceptId = source.ConceptId,
            AccountId = targetAccountId,
            Direction = source.Direction == EntryDirection.In ? EntryDirection.Out : EntryDirection.In,
            IsTransfer = true,
            Status = source.Status,
            DueDate = source.DueDate,
            ValueDate = source.ValueDate,
            ForecastAmount = source.ForecastAmount,
            ActualAmount = source.ActualAmount,
            Description = source.Description,
            ImportRowId = source.ImportRowId,
            Fingerprint = null
        };

        /// <summary>
        /// Deja los espejos de la cuenta al día tras cambiar su enlace: quita los
        /// del concepto anterior y crea los que falten del actual. Sólo toca
        /// periodos abiertos; los cerrados no se reescriben. No guarda cambios.
        /// </summary>
        public static async Task SyncAccountAsync(
            IAppDbContext db, Account account, Guid? previousConceptId, CancellationToken ct)
        {
            var openPeriodIds = await db.MonthlyPeriods
                .Where(period => period.UserId == account.UserId && period.Status != PeriodStatus.Closed)
                .Select(period => period.Id)
                .ToListAsync(ct);
            if (openPeriodIds.Count == 0) return;

            if (previousConceptId is not null && previousConceptId != account.TransferConceptId)
            {
                var staleMirrors = await db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == account.UserId &&
                        entry.AccountId == account.Id &&
                        entry.ConceptId == previousConceptId &&
                        entry.IsTransfer &&
                        entry.ImportRowId != null &&
                        entry.Fingerprint == null &&
                        openPeriodIds.Contains(entry.PeriodId))
                    .ToListAsync(ct);
                db.LedgerEntries.RemoveRange(staleMirrors);
            }

            if (!account.IsActive || account.TransferConceptId is null) return;

            var conceptId = account.TransferConceptId.Value;
            var sources = await db.LedgerEntries
                .Where(entry =>
                    entry.UserId == account.UserId &&
                    entry.ConceptId == conceptId &&
                    entry.IsTransfer &&
                    entry.AccountId != null &&
                    entry.AccountId != account.Id &&
                    entry.ImportRowId != null &&
                    entry.Fingerprint != null &&
                    openPeriodIds.Contains(entry.PeriodId))
                .ToListAsync(ct);
            if (sources.Count == 0) return;

            var mirroredRows = (await db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == account.UserId &&
                        entry.AccountId == account.Id &&
                        entry.ConceptId == conceptId &&
                        entry.ImportRowId != null)
                    .Select(entry => entry.ImportRowId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();

            foreach (var source in sources)
            {
                if (!mirroredRows.Add(source.ImportRowId!.Value)) continue;
                db.LedgerEntries.Add(Create(source, account.Id));
            }
        }
    }
}
