using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ImportSplitLink(ImportRowAllocation Part, ImportRow Row);

    public static class ImportSplitLinks
    {
        public static IEnumerable<ImportSplitLink> Project(IEnumerable<ImportRow> rows) =>
            rows.SelectMany(row => Project(row, row.Allocations));

        public static IEnumerable<ImportSplitLink> Project(
            ImportRow row, IEnumerable<ImportRowAllocation> parts) =>
            parts.Where(part => part.PlannedEntryId.HasValue || part.SettlementId.HasValue)
                .Select(part => new ImportSplitLink(part, new ImportRow
                {
                    Id = row.Id,
                    UserId = row.UserId,
                    BatchId = row.BatchId,
                    RowNumber = row.RowNumber,
                    ValueDate = row.ValueDate,
                    SettledDate = row.SettledDate,
                    Amount = Math.Sign(row.Amount) * part.Amount,
                    ClassifiedStatus = row.ClassifiedStatus,
                    Currency = row.Currency,
                    RawDescription = row.RawDescription,
                    Fingerprint = row.Fingerprint,
                    Status = row.Status,
                    ConfirmedConceptId = part.ConceptId,
                    SettlementId = part.SettlementId,
                    PlannedAllocations = part.PlannedEntryId is { } plannedId
                        ? new List<ImportRowPlannedAllocation>
                        {
                            new() { PlannedEntryId = plannedId, Amount = part.Amount }
                        }
                        : new List<ImportRowPlannedAllocation>()
                }));

        public static async Task<string?> ValidateReviewAsync(
            IAppDbContext db, Guid userId, ImportRow row,
            IReadOnlyCollection<ImportRowAllocation> parts, CancellationToken ct)
        {
            if (parts.Any(part => part.CounterpartyId.HasValue ||
                (part.PlannedEntryId.HasValue && part.SettlementId.HasValue)))
                return "Cada parte admite un previsto o una liquidación, no ambos.";

            var links = Project(row, parts).ToList();
            if (links.Count == 0) return null;
            if (!ImportBatchApplicationService.EntersAsPaid(row))
                return "Solo puedes vincular partes de un movimiento bancario ya cobrado o pagado.";
            if (row.PasanacoParticipantId is not null)
                return "Quita la cuota del pasanaco antes de vincular partes a otros pendientes.";

            var otherRows = await db.ImportRows.AsNoTracking()
                .Include(item => item.Allocations).Include(item => item.PlannedAllocations)
                .Where(item => item.UserId == userId && item.BatchId == row.BatchId && item.Id != row.Id)
                .ToListAsync(ct);
            var effectiveRows = otherRows.Concat(Project(otherRows).Select(link => link.Row))
                .Concat(links.Select(link => link.Row)).ToList();
            var plannedIds = parts.Where(part => part.PlannedEntryId.HasValue)
                .Select(part => part.PlannedEntryId!.Value).ToHashSet();
            var planned = await db.LedgerEntries.AsNoTracking()
                .Include(entry => entry.Period).Include(entry => entry.Concept)
                .Where(entry => entry.UserId == userId && plannedIds.Contains(entry.Id))
                .ToDictionaryAsync(entry => entry.Id, ct);
            foreach (var link in links.Where(link => link.Part.PlannedEntryId.HasValue))
            {
                var allocations = ImportPlannedLinks.Links(link.Row).ToList();
                var error = ImportPlannedLinks.AllocationError(link.Row, allocations, planned);
                if (error is not null) return error;
                var target = planned[link.Part.PlannedEntryId!.Value];
                if (target.ConceptId != link.Part.ConceptId)
                    return "La categoría de una parte no coincide con la de su previsto.";
                var total = effectiveRows.Where(ImportPlannedLinks.Deducts)
                    .SelectMany(ImportPlannedLinks.Links)
                    .Where(part => part.PlannedEntryId == target.Id).Sum(part => part.Amount);
                if (total > target.ForecastAmount + ImportPlannedLinks.CoverTolerance)
                    return "El reparto supera lo pendiente de un previsto. Revisa las otras filas vinculadas.";
                error = await ImportPlannedLinks.CareAllocationErrorAsync(db, userId, allocations, planned, ct);
                if (error is not null) return error;
            }
            return (await ImportObligationLinks.ValidateAsync(db, userId, effectiveRows, ct)).Error;
        }
    }
}
