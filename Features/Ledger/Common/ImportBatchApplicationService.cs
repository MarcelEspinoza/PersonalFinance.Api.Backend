using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ImportApplicationResult(
        bool Success,
        int Applied,
        Guid BatchId,
        string? Error = null,
        int UnassignedRows = 0,
        bool Conflict = false,
        bool NotFound = false);

    public interface IImportBatchApplicationService
    {
        Task<ImportApplicationResult> ApplyAsync(Guid userId, Guid batchId, CancellationToken ct);
    }

    public sealed class ImportBatchApplicationService : IImportBatchApplicationService
    {
        private readonly IAppDbContext _db;
        private readonly PeriodProvisioner _periods;

        public ImportBatchApplicationService(IAppDbContext db, PeriodProvisioner periods)
        {
            _db = db;
            _periods = periods;
        }

        public async Task<ImportApplicationResult> ApplyAsync(
            Guid userId,
            Guid batchId,
            CancellationToken ct)
        {
            var batch = await _db.ImportBatches
                .Include(item => item.Rows)
                .FirstOrDefaultAsync(
                    item => item.Id == batchId && item.UserId == userId,
                    ct);
            if (batch is null)
                return new(false, 0, batchId, NotFound: true);
            if (batch.Status == ImportBatchStatus.Applied)
                return new(false, 0, batchId, "El lote ya se ha aplicado.", Conflict: true);
            if (batch.Rows.Count == 0)
                return new(false, 0, batchId, "El lote no contiene movimientos para aplicar.");

            var unassigned = batch.Rows.Count(row =>
                row.Status == ImportRowStatus.Pending &&
                row.ConfirmedConceptId is null &&
                row.SuggestedConceptId is null);
            if (unassigned > 0)
                return new(false, 0, batchId, "Hay filas sin concepto asignado.", unassigned);

            var accountId = batch.AccountId;
            if (accountId is null)
                return new(false, 0, batchId, "El lote no tiene cuenta.");

            var concepts = await _db.Concepts
                .Where(concept => concept.UserId == userId)
                .ToDictionaryAsync(concept => concept.Id, ct);
            var feeConcept = concepts.Values.FirstOrDefault(concept =>
                string.Equals(
                    concept.Name,
                    "Comisiones bancarias",
                    StringComparison.OrdinalIgnoreCase));
            var existingFingerprints = (await _db.LedgerEntries
                    .Where(entry => entry.UserId == userId && entry.Fingerprint != null)
                    .Select(entry => entry.Fingerprint!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var periods = new Dictionary<(int Year, int Month), MonthlyPeriod>();
            foreach (var month in batch.Rows
                         .Where(row => row.Status == ImportRowStatus.Pending)
                         .Select(row => (row.ValueDate.Year, row.ValueDate.Month))
                         .Distinct())
            {
                var period = await _periods.GetOrOpenAsync(
                    userId,
                    month.Year,
                    month.Month,
                    ct);
                if (period.Status == PeriodStatus.Closed)
                    return new(
                        false,
                        0,
                        batchId,
                        $"El periodo {period.Year}-{period.Month:D2} está cerrado.",
                        Conflict: true);

                periods[month] = period;
            }

            var applied = 0;
            foreach (var row in batch.Rows.Where(row => row.Status == ImportRowStatus.Pending))
            {
                var conceptId = row.ConfirmedConceptId ?? row.SuggestedConceptId;
                if (conceptId is null || !concepts.TryGetValue(conceptId.Value, out var concept))
                    continue;
                if (!existingFingerprints.Add(row.Fingerprint))
                {
                    row.Status = ImportRowStatus.Duplicate;
                    continue;
                }
                if (row.Fee != 0m && feeConcept is null)
                    return new(
                        false,
                        0,
                        batchId,
                        "Falta el concepto 'Comisiones bancarias': prepara el plan de cuentas antes de aplicar.");

                var period = periods[(row.ValueDate.Year, row.ValueDate.Month)];

                var isPaid = row.ClassifiedStatus == EntryStatus.Paid;
                var amount = Math.Abs(row.Amount);
                _db.LedgerEntries.Add(new LedgerEntry
                {
                    UserId = userId,
                    PeriodId = period.Id,
                    ConceptId = concept.Id,
                    AccountId = accountId,
                    Direction = row.Amount >= 0 ? EntryDirection.In : EntryDirection.Out,
                    IsTransfer = concept.Kind == ConceptKind.Transfer,
                    Status = row.ClassifiedStatus,
                    DueDate = row.ValueDate,
                    ValueDate = isPaid ? row.ValueDate : null,
                    ForecastAmount = amount,
                    ActualAmount = isPaid ? amount : null,
                    Description = row.RawDescription,
                    ImportRowId = row.Id,
                    Fingerprint = row.Fingerprint
                });

                if (row.Fee != 0m && feeConcept is not null)
                {
                    var feeFingerprint = CreateFeeFingerprint(row.Fingerprint);
                    if (existingFingerprints.Add(feeFingerprint))
                    {
                        _db.LedgerEntries.Add(new LedgerEntry
                        {
                            UserId = userId,
                            PeriodId = period.Id,
                            ConceptId = feeConcept.Id,
                            AccountId = accountId,
                            Direction = EntryDirection.Out,
                            IsTransfer = false,
                            Status = row.ClassifiedStatus,
                            DueDate = row.ValueDate,
                            ValueDate = isPaid ? row.ValueDate : null,
                            ForecastAmount = row.Fee,
                            ActualAmount = isPaid ? row.Fee : null,
                            Description = $"Comisión: {row.RawDescription}",
                            ImportRowId = row.Id,
                            Fingerprint = feeFingerprint
                        });
                    }
                }

                row.Status = ImportRowStatus.Accepted;
                applied++;
            }

            batch.AcceptedRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Accepted);
            batch.DuplicateRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Duplicate);
            batch.Status = ImportBatchStatus.Applied;
            batch.AppliedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new(true, applied, batch.Id);
        }

        private static string CreateFeeFingerprint(string rowFingerprint)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{rowFingerprint}|fee"));
            return Convert.ToHexString(bytes);
        }
    }
}
