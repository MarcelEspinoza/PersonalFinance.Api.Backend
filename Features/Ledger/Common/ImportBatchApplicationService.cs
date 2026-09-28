using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Calculations;
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
        bool NotFound = false,
        int MatchedForecasts = 0);

    public interface IImportBatchApplicationService
    {
        Task<ImportApplicationResult> ApplyAsync(Guid userId, Guid batchId, CancellationToken ct);
    }

    public sealed class ImportBatchApplicationService : IImportBatchApplicationService
    {
        private const int ForecastMatchWindowDays = 5;

        private sealed record ForecastMatch(
            ImportRow Row,
            Guid ConceptId,
            EntryDirection Direction,
            DateOnly DueDate,
            LedgerEntry? Entry,
            RecurringRule? Rule)
        {
            public string Key => Entry is not null
                ? $"entry:{Entry.Id}"
                : $"rule:{Rule!.Id}:{DueDate.Year:D4}-{DueDate.Month:D2}";
        }

        private sealed record ForecastMatchResult(List<ForecastMatch> Matches, int? AmbiguousRowNumber);

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
            var matchResult = await FindForecastMatchesAsync(
                userId,
                accountId.Value,
                batch.Rows.Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    row.ClassifiedStatus == EntryStatus.Paid &&
                    !existingFingerprints.Contains(row.Fingerprint)),
                ct);
            if (matchResult.AmbiguousRowNumber is int ambiguousRowNumber)
            {
                return new(
                    false,
                    0,
                    batchId,
                    $"La fila {ambiguousRowNumber} coincide con varias previsiones cercanas. Revisa el concepto o la fecha antes de aplicar.",
                    Conflict: true);
            }
            var plannedMatches = matchResult.Matches;

            var duplicateMatch = plannedMatches
                .GroupBy(match => match.Key, StringComparer.Ordinal)
                .FirstOrDefault(group => group.Count() > 1);
            if (duplicateMatch is not null)
            {
                return new(
                    false,
                    0,
                    batchId,
                    "Varios movimientos del archivo coinciden con la misma previsión. Revisa las filas antes de aplicar.",
                    Conflict: true);
            }

            var matchByRowId = plannedMatches.ToDictionary(match => match.Row.Id);
            var periods = new Dictionary<(int Year, int Month), MonthlyPeriod>();
            foreach (var month in batch.Rows
                         .Where(row => row.Status == ImportRowStatus.Pending)
                         .Where(row => !existingFingerprints.Contains(row.Fingerprint) && !matchByRowId.ContainsKey(row.Id))
                         .Select(row => (row.ValueDate.Year, row.ValueDate.Month))
                         .Concat(plannedMatches.Select(match => (match.DueDate.Year, match.DueDate.Month)))
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

            foreach (var match in plannedMatches.Where(match => match.Entry is null))
            {
                var period = periods[(match.DueDate.Year, match.DueDate.Month)];
                if (period.Status == PeriodStatus.Closed)
                {
                    return new(
                        false,
                        0,
                        batchId,
                        $"El periodo {period.Year}-{period.Month:D2} de una previsión coincidente está cerrado.",
                        Conflict: true);
                }
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

                var isPaid = row.ClassifiedStatus == EntryStatus.Paid;
                var amount = Math.Abs(row.Amount);
                if (isPaid && matchByRowId.TryGetValue(row.Id, out var match))
                {
                    var forecastEntry = match.Entry ?? await _db.LedgerEntries
                        .FirstOrDefaultAsync(entry =>
                            entry.UserId == userId &&
                            entry.RecurringRuleId == match.Rule!.Id &&
                            entry.DueDate == match.DueDate,
                            ct);
                    if (forecastEntry is null ||
                        forecastEntry.Status is EntryStatus.Paid or EntryStatus.Skipped)
                    {
                        return new(
                            false,
                            0,
                            batchId,
                            "La previsión coincidente ya no está disponible. Actualiza la revisión del archivo antes de aplicar.",
                            Conflict: true);
                    }

                    forecastEntry.Status = EntryStatus.Paid;
                    forecastEntry.ActualAmount = amount;
                    forecastEntry.ValueDate = row.ValueDate;
                    forecastEntry.AccountId = accountId;
                    forecastEntry.ImportRowId = row.Id;
                    forecastEntry.Fingerprint = row.Fingerprint;
                    forecastEntry.Description = row.RawDescription;
                    forecastEntry.UpdatedAt = DateTime.UtcNow;
                    row.LedgerEntryId = forecastEntry.Id;

                    var account = await _db.Accounts.FirstOrDefaultAsync(
                        item => item.Id == accountId && item.UserId == userId,
                        ct);
                    if (account is not null &&
                        row.ValueDate < account.OpeningDate &&
                        forecastEntry.DueDate >= account.OpeningDate)
                    {
                        account.OpeningBalance += forecastEntry.Direction == EntryDirection.In
                            ? amount
                            : -amount;
                    }

                    if (row.Fee != 0m && feeConcept is not null)
                    {
                        var feeFingerprint = CreateFeeFingerprint(row.Fingerprint);
                        if (existingFingerprints.Add(feeFingerprint))
                        {
                            var period = periods[(match.DueDate.Year, match.DueDate.Month)];
                            _db.LedgerEntries.Add(new LedgerEntry
                            {
                                UserId = userId,
                                PeriodId = period.Id,
                                ConceptId = feeConcept.Id,
                                AccountId = accountId,
                                Direction = EntryDirection.Out,
                                Status = EntryStatus.Paid,
                                DueDate = match.DueDate,
                                ValueDate = row.ValueDate,
                                ForecastAmount = row.Fee,
                                ActualAmount = row.Fee,
                                Description = $"Comisión: {row.RawDescription}",
                                ImportRowId = row.Id,
                                Fingerprint = feeFingerprint
                            });
                            if (account is not null &&
                                row.ValueDate < account.OpeningDate &&
                                match.DueDate >= account.OpeningDate)
                            {
                                account.OpeningBalance -= row.Fee;
                            }
                        }
                    }
                }
                else
                {
                    var period = periods[(row.ValueDate.Year, row.ValueDate.Month)];
                    var importedEntry = new LedgerEntry
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
                    };
                    _db.LedgerEntries.Add(importedEntry);
                    row.LedgerEntryId = importedEntry.Id;

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
                }

                row.Status = ImportRowStatus.Accepted;
                applied++;
            }

            batch.AcceptedRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Accepted);
            batch.DuplicateRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Duplicate);
            batch.Status = ImportBatchStatus.Applied;
            batch.AppliedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new(true, applied, batch.Id, MatchedForecasts: plannedMatches.Count);
        }

        private async Task<ForecastMatchResult> FindForecastMatchesAsync(
            Guid userId,
            Guid accountId,
            IEnumerable<ImportRow> rows,
            CancellationToken ct)
        {
            var candidates = rows
                .Where(row => row.ConfirmedConceptId.HasValue || row.SuggestedConceptId.HasValue)
                .ToList();
            if (candidates.Count == 0) return new(new(), null);

            var firstDate = candidates.Min(row => row.ValueDate).AddDays(-ForecastMatchWindowDays);
            var lastDate = candidates.Max(row => row.ValueDate).AddDays(ForecastMatchWindowDays);
            var conceptIds = candidates
                .Select(row => row.ConfirmedConceptId ?? row.SuggestedConceptId!.Value)
                .ToHashSet();
            var directionByRow = candidates.ToDictionary(
                row => row.Id,
                row => row.Amount >= 0 ? EntryDirection.In : EntryDirection.Out);

            var existingEntries = await _db.LedgerEntries
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.AccountId == accountId &&
                    conceptIds.Contains(entry.ConceptId) &&
                    entry.DueDate >= firstDate &&
                    entry.DueDate <= lastDate)
                .ToListAsync(ct);
            var availableEntries = existingEntries
                .Where(entry => entry.Status is not (EntryStatus.Paid or EntryStatus.Skipped))
                .ToList();
            var rules = await _db.RecurringRules
                .Where(rule =>
                    rule.UserId == userId &&
                    rule.IsActive &&
                    rule.AccountId == accountId &&
                    conceptIds.Contains(rule.ConceptId))
                .ToListAsync(ct);
            var matches = new List<ForecastMatch>();

            foreach (var row in candidates)
            {
                var conceptId = row.ConfirmedConceptId ?? row.SuggestedConceptId!.Value;
                var direction = directionByRow[row.Id];
                var earliest = row.ValueDate.AddDays(-ForecastMatchWindowDays);
                var latest = row.ValueDate.AddDays(ForecastMatchWindowDays);
                var rowMatches = availableEntries
                    .Where(entry =>
                        entry.ConceptId == conceptId &&
                        entry.Direction == direction &&
                        Math.Abs(entry.DueDate.DayNumber - row.ValueDate.DayNumber) <= ForecastMatchWindowDays)
                    .Select(entry => new ForecastMatch(row, conceptId, direction, entry.DueDate, entry, null))
                    .ToList();

                foreach (var rule in rules.Where(rule =>
                             rule.ConceptId == conceptId &&
                             rule.Direction == direction &&
                             rule.StartDate <= latest &&
                             (rule.EndDate is null || rule.EndDate >= earliest)))
                {
                    for (var cursor = new DateOnly(earliest.Year, earliest.Month, 1);
                         cursor <= latest;
                         cursor = cursor.AddMonths(1))
                    {
                        if (!RecurrenceCalculator.OccursIn(rule, cursor.Year, cursor.Month)) continue;
                        var dueDate = RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, cursor.Year, cursor.Month);
                        if (Math.Abs(dueDate.DayNumber - row.ValueDate.DayNumber) > ForecastMatchWindowDays)
                            continue;

                        var occurrenceExists = existingEntries.Any(entry =>
                            entry.RecurringRuleId == rule.Id &&
                            entry.DueDate.Year == dueDate.Year &&
                            entry.DueDate.Month == dueDate.Month);
                        if (!occurrenceExists)
                        {
                            rowMatches.Add(new ForecastMatch(row, conceptId, direction, dueDate, null, rule));
                        }
                    }
                }

                if (rowMatches.Count > 1)
                {
                    return new(matches, row.RowNumber);
                }

                if (rowMatches.Count == 1) matches.Add(rowMatches[0]);
            }

            return new(matches, null);
        }

        private static string CreateFeeFingerprint(string rowFingerprint)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{rowFingerprint}|fee"));
            return Convert.ToHexString(bytes);
        }
    }
}
