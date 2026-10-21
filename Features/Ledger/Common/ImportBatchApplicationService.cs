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

        /// <summary>
        /// Con qué previsión se emparejaría cada fila si el lote se aplicara ahora.
        /// No modifica nada: sirve para revisar el extracto antes de aplicarlo.
        /// </summary>
        Task<IReadOnlyList<ImportForecastPreview>> PreviewForecastMatchesAsync(
            Guid userId, ImportBatch batch, CancellationToken ct);
    }

    /// <summary>Previsión que absorbería una fila: un asiento ya generado o una regla aún sin generar.</summary>
    public sealed record ImportForecastPreview(
        Guid RowId,
        DateOnly DueDate,
        decimal ForecastAmount,
        Guid? EntryId,
        Guid? RuleId);

    public sealed class ImportBatchApplicationService : IImportBatchApplicationService
    {
        private const int ForecastMatchWindowDays = 5;
        private const decimal MinForecastAmountRatio = 0.5m;
        private const decimal MaxForecastAmountRatio = 2m;

        private sealed record ForecastMatch(
            ImportRow Row,
            Guid ConceptId,
            EntryDirection Direction,
            DateOnly DueDate,
            decimal ForecastAmount,
            LedgerEntry? Entry,
            RecurringRule? Rule)
        {
            public string Key => Entry is not null
                ? $"entry:{Entry.Id}"
                : $"rule:{Rule!.Id}:{DueDate.Year:D4}-{DueDate.Month:D2}";

            public decimal AmountGap => Math.Abs(Math.Abs(Row.Amount) - ForecastAmount);

            public int DayGap => Math.Abs(DueDate.DayNumber - Row.ValueDate.DayNumber);
        }

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
                    .ThenInclude(row => row.Allocations)
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
                row.Allocations.Count == 0 &&
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
            var mirrorAccountByConcept = (await _db.Accounts
                    .Where(account =>
                        account.UserId == userId &&
                        account.IsActive &&
                        account.TransferConceptId != null)
                    .OrderBy(account => account.CreatedAt)
                    .ToListAsync(ct))
                .GroupBy(account => account.TransferConceptId!.Value)
                .ToDictionary(group => group.Key, group => group.First());
            var existingFingerprints = (await _db.LedgerEntries
                    .Where(entry => entry.UserId == userId && entry.Fingerprint != null)
                    .Select(entry => entry.Fingerprint!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var plannedMatches = await FindForecastMatchesAsync(
                userId,
                accountId.Value,
                batch.Rows.Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    EntersAsPaid(row) &&
                    row.Allocations.Count == 0 &&
                    !existingFingerprints.Contains(row.Fingerprint)),
                ct);
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

            // Al abrir el mes, cada regla ya generó su previsión: se busca en todo el mes porque
            // el día pudo cambiar. Si no aparece, el movimiento entra como nuevo (sin bloquear).
            var ruleEntryByRowId = new Dictionary<Guid, LedgerEntry>();
            foreach (var match in plannedMatches.Where(match => match.Entry is null))
            {
                var ruleId = match.Rule!.Id;
                var matchMonth = new DateOnly(match.DueDate.Year, match.DueDate.Month, 1);
                var afterMatchMonth = matchMonth.AddMonths(1);
                var ruleEntry = await _db.LedgerEntries
                    .FirstOrDefaultAsync(entry =>
                        entry.UserId == userId &&
                        entry.RecurringRuleId == ruleId &&
                        entry.DueDate >= matchMonth &&
                        entry.DueDate < afterMatchMonth &&
                        entry.Status != EntryStatus.Paid &&
                        entry.Status != EntryStatus.Skipped,
                        ct);
                if (ruleEntry is not null)
                {
                    ruleEntryByRowId[match.Row.Id] = ruleEntry;
                    continue;
                }

                matchByRowId.Remove(match.Row.Id);
                var rowMonth = (match.Row.ValueDate.Year, match.Row.ValueDate.Month);
                if (periods.ContainsKey(rowMonth)) continue;

                var rowPeriod = await _periods.GetOrOpenAsync(userId, rowMonth.Year, rowMonth.Month, ct);
                if (rowPeriod.Status == PeriodStatus.Closed)
                    return new(
                        false,
                        0,
                        batchId,
                        $"El periodo {rowPeriod.Year}-{rowPeriod.Month:D2} está cerrado.",
                        Conflict: true);

                periods[rowMonth] = rowPeriod;
            }

            var applied = 0;
            var matchedForecasts = 0;
            foreach (var row in batch.Rows.Where(row => row.Status == ImportRowStatus.Pending))
            {
                var isSplit = row.Allocations.Count > 0;
                var conceptId = isSplit ? null : row.ConfirmedConceptId ?? row.SuggestedConceptId;
                if (!isSplit && (conceptId is null || !concepts.TryGetValue(conceptId.Value, out _)))
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

                var isPaid = EntersAsPaid(row);
                var amount = Math.Abs(row.Amount);
                if (isSplit)
                {
                    if (row.Allocations.Count is < 2 or > 3 ||
                        row.Allocations.Select(allocation => allocation.ConceptId).Distinct().Count() != row.Allocations.Count ||
                        row.Allocations.Sum(allocation => allocation.Amount) != amount ||
                        row.Allocations.Any(allocation =>
                            allocation.Amount <= 0m ||
                            decimal.Round(allocation.Amount, 2) != allocation.Amount ||
                            !concepts.TryGetValue(allocation.ConceptId, out var allocationConcept) ||
                            !allocationConcept.IsActive ||
                            allocationConcept.Kind != (row.Amount > 0m ? ConceptKind.Income : ConceptKind.Expense)))
                    {
                        return new(
                            false,
                            0,
                            batchId,
                            $"La división de la fila {row.RowNumber} ya no es válida. Vuelve a revisarla.",
                            Conflict: true);
                    }

                    var period = periods[(row.ValueDate.Year, row.ValueDate.Month)];
                    var direction = row.Amount > 0m ? EntryDirection.In : EntryDirection.Out;
                    var firstEntry = true;
                    foreach (var allocation in row.Allocations.OrderBy(item => item.SortOrder))
                    {
                        var allocationEntry = new LedgerEntry
                        {
                            UserId = userId,
                            PeriodId = period.Id,
                            ConceptId = allocation.ConceptId,
                            AccountId = accountId,
                            Direction = direction,
                            Status = isPaid ? EntryStatus.Paid : row.ClassifiedStatus,
                            DueDate = row.ValueDate,
                            ValueDate = isPaid ? SettledOn(row) : null,
                            ForecastAmount = allocation.Amount,
                            ActualAmount = isPaid ? allocation.Amount : null,
                            Description = row.RawDescription,
                            ImportRowId = row.Id,
                            Fingerprint = firstEntry ? row.Fingerprint : null
                        };
                        _db.LedgerEntries.Add(allocationEntry);
                        if (firstEntry) row.LedgerEntryId = allocationEntry.Id;
                        firstEntry = false;
                    }

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
                                Status = isPaid ? EntryStatus.Paid : row.ClassifiedStatus,
                                DueDate = row.ValueDate,
                                ValueDate = isPaid ? SettledOn(row) : null,
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
                    continue;
                }

                var concept = concepts[conceptId!.Value];
                var match = isPaid && matchByRowId.TryGetValue(row.Id, out var plannedMatch) ? plannedMatch : null;
                var forecastEntry = match is null ? null : match.Entry ?? ruleEntryByRowId[row.Id];
                if (match is not null && forecastEntry is not null)
                {
                    if (forecastEntry.Status is EntryStatus.Paid or EntryStatus.Skipped)
                    {
                        return new(
                            false,
                            0,
                            batchId,
                            "La previsión coincidente ya no está disponible. Actualiza la revisión del archivo antes de aplicar.",
                            Conflict: true);
                    }

                    matchedForecasts++;
                    forecastEntry.Status = EntryStatus.Paid;
                    forecastEntry.ActualAmount = amount;
                    forecastEntry.ValueDate = SettledOn(row);
                    forecastEntry.AccountId = accountId;
                    forecastEntry.IsTransfer = concept.Kind == ConceptKind.Transfer;
                    forecastEntry.ImportRowId = row.Id;
                    forecastEntry.Fingerprint = row.Fingerprint;
                    forecastEntry.Description = row.RawDescription;
                    forecastEntry.UpdatedAt = DateTime.UtcNow;
                    row.LedgerEntryId = forecastEntry.Id;
                    AddTransferMirror(forecastEntry, mirrorAccountByConcept);

                    // El saldo inicial es el real del banco en la fecha de apertura: un movimiento
                    // anterior ya está incluido en él, así que emparejarlo no debe modificarlo.
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
                                ValueDate = SettledOn(row),
                                ForecastAmount = row.Fee,
                                ActualAmount = row.Fee,
                                Description = $"Comisión: {row.RawDescription}",
                                ImportRowId = row.Id,
                                Fingerprint = feeFingerprint
                            });
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
                        Status = isPaid ? EntryStatus.Paid : row.ClassifiedStatus,
                        DueDate = row.ValueDate,
                        ValueDate = isPaid ? SettledOn(row) : null,
                        ForecastAmount = amount,
                        ActualAmount = isPaid ? amount : null,
                        Description = row.RawDescription,
                        ImportRowId = row.Id,
                        Fingerprint = row.Fingerprint
                    };
                    _db.LedgerEntries.Add(importedEntry);
                    row.LedgerEntryId = importedEntry.Id;
                    AddTransferMirror(importedEntry, mirrorAccountByConcept);

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
                                Status = isPaid ? EntryStatus.Paid : row.ClassifiedStatus,
                                DueDate = row.ValueDate,
                                ValueDate = isPaid ? SettledOn(row) : null,
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

            return new(true, applied, batch.Id, MatchedForecasts: matchedForecasts);
        }

        public async Task<IReadOnlyList<ImportForecastPreview>> PreviewForecastMatchesAsync(
            Guid userId, ImportBatch batch, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (batch.Status == ImportBatchStatus.Applied || batch.AccountId is null)
                return Array.Empty<ImportForecastPreview>();

            // Mismo filtro que ApplyAsync: lo que ya está en el libro no se vuelve a emparejar.
            var fingerprints = batch.Rows.Select(row => row.Fingerprint).ToList();
            var existingFingerprints = (await _db.LedgerEntries
                    .AsNoTracking()
                    .Where(entry =>
                        entry.UserId == userId &&
                        entry.Fingerprint != null &&
                        fingerprints.Contains(entry.Fingerprint))
                    .Select(entry => entry.Fingerprint!)
                    .ToListAsync(ct))
                .ToHashSet(StringComparer.Ordinal);
            var matches = await FindForecastMatchesAsync(
                userId,
                batch.AccountId.Value,
                batch.Rows.Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    EntersAsPaid(row) &&
                    row.Allocations.Count == 0 &&
                    !existingFingerprints.Contains(row.Fingerprint)),
                ct);

            return matches
                .Select(match => new ImportForecastPreview(
                    match.Row.Id,
                    match.DueDate,
                    match.ForecastAmount,
                    match.Entry?.Id,
                    match.Rule?.Id))
                .ToList();
        }

        /// <summary>
        /// Un traspaso hacia una cuenta sin extracto propio (la hucha) también
        /// mueve su saldo: se añade el asiento contrario en esa cuenta.
        /// </summary>
        private void AddTransferMirror(
            LedgerEntry source,
            IReadOnlyDictionary<Guid, Account> mirrorAccountByConcept)
        {
            if (mirrorAccountByConcept.TryGetValue(source.ConceptId, out var target) &&
                TransferMirror.ShouldMirror(source, target))
            {
                _db.LedgerEntries.Add(TransferMirror.Create(source, target.Id));
            }
        }

        private async Task<List<ForecastMatch>> FindForecastMatchesAsync(
            Guid userId,
            Guid accountId,
            IEnumerable<ImportRow> rows,
            CancellationToken ct)
        {
            var candidates = rows
                .Where(row => row.ConfirmedConceptId.HasValue || row.SuggestedConceptId.HasValue)
                .ToList();
            if (candidates.Count == 0) return new();

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
            // Lo que ya generó cada regla en esos meses, en la cuenta que sea: si la regla cambió
            // de cuenta o de día, la previsión de ese mes sigue siendo la que se generó entonces.
            var ruleIds = rules.Select(rule => (Guid?)rule.Id).ToList();
            var firstMonth = new DateOnly(firstDate.Year, firstDate.Month, 1);
            var afterLastMonth = new DateOnly(lastDate.Year, lastDate.Month, 1).AddMonths(1);
            var ruleOccurrences = ruleIds.Count == 0
                ? new List<LedgerEntry>()
                : await _db.LedgerEntries
                    .Where(entry =>
                        entry.UserId == userId &&
                        ruleIds.Contains(entry.RecurringRuleId) &&
                        entry.DueDate >= firstMonth &&
                        entry.DueDate < afterLastMonth)
                    .ToListAsync(ct);
            var options = new List<ForecastMatch>();

            foreach (var row in candidates)
            {
                var conceptId = row.ConfirmedConceptId ?? row.SuggestedConceptId!.Value;
                var direction = directionByRow[row.Id];
                var earliest = row.ValueDate.AddDays(-ForecastMatchWindowDays);
                var latest = row.ValueDate.AddDays(ForecastMatchWindowDays);
                var amount = Math.Abs(row.Amount);
                // Un gasto de fin de mes es de ese mes: no se adelanta a la previsión del mes
                // siguiente. Un ingreso sí puede (la nómina del día 28 es la del mes que viene).
                var nextMonth = new DateOnly(row.ValueDate.Year, row.ValueDate.Month, 1).AddMonths(1);
                bool InAllowedMonth(DateOnly dueDate) => direction == EntryDirection.In || dueDate < nextMonth;

                options.AddRange(availableEntries
                    .Where(entry =>
                        entry.ConceptId == conceptId &&
                        entry.Direction == direction &&
                        Math.Abs(entry.DueDate.DayNumber - row.ValueDate.DayNumber) <= ForecastMatchWindowDays &&
                        InAllowedMonth(entry.DueDate) &&
                        IsPlausibleAmount(amount, entry.ForecastAmount))
                    .Select(entry => new ForecastMatch(
                        row, conceptId, direction, entry.DueDate, entry.ForecastAmount, entry, null)));

                foreach (var rule in rules.Where(rule =>
                             rule.ConceptId == conceptId &&
                             rule.Direction == direction &&
                             rule.StartDate <= latest &&
                             (rule.EndDate is null || rule.EndDate >= earliest) &&
                             IsPlausibleAmount(amount, rule.ForecastAmount)))
                {
                    for (var cursor = new DateOnly(earliest.Year, earliest.Month, 1);
                         cursor <= latest;
                         cursor = cursor.AddMonths(1))
                    {
                        if (!RecurrenceCalculator.OccursIn(rule, cursor.Year, cursor.Month)) continue;
                        var dueDate = RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, cursor.Year, cursor.Month);
                        if (Math.Abs(dueDate.DayNumber - row.ValueDate.DayNumber) > ForecastMatchWindowDays ||
                            !InAllowedMonth(dueDate))
                            continue;

                        var occurrence = ruleOccurrences.FirstOrDefault(entry =>
                            entry.RecurringRuleId == rule.Id &&
                            entry.DueDate.Year == dueDate.Year &&
                            entry.DueDate.Month == dueDate.Month);
                        if (occurrence is null)
                        {
                            options.Add(new ForecastMatch(
                                row, conceptId, direction, dueDate, rule.ForecastAmount, null, rule));
                        }
                        else if (occurrence.Status is not (EntryStatus.Paid or EntryStatus.Skipped) &&
                                 occurrence.ConceptId == conceptId &&
                                 occurrence.Direction == direction &&
                                 IsPlausibleAmount(amount, occurrence.ForecastAmount) &&
                                 !options.Any(option => option.Row.Id == row.Id && option.Entry?.Id == occurrence.Id))
                        {
                            options.Add(new ForecastMatch(
                                row, conceptId, direction, occurrence.DueDate, occurrence.ForecastAmount, occurrence, null));
                        }
                    }
                }
            }

            // Reparto uno a uno: gana la pareja con el importe más parecido y, si empatan,
            // la de fecha más cercana. Lo que no encaje se importa como movimiento nuevo.
            var matches = new List<ForecastMatch>();
            var matchedRows = new HashSet<Guid>();
            var usedForecasts = new HashSet<string>(StringComparer.Ordinal);
            foreach (var option in options
                         .OrderBy(option => option.AmountGap)
                         .ThenBy(option => option.DayGap)
                         .ThenBy(option => option.DueDate)
                         .ThenBy(option => option.Row.RowNumber)
                         .ThenBy(option => option.Key, StringComparer.Ordinal))
            {
                if (matchedRows.Contains(option.Row.Id) || !usedForecasts.Add(option.Key)) continue;
                matchedRows.Add(option.Row.Id);
                matches.Add(option);
            }

            return matches;
        }

        private static bool IsPlausibleAmount(decimal actual, decimal forecast) =>
            forecast <= 0m ||
            (actual >= forecast * MinForecastAmountRatio && actual <= forecast * MaxForecastAmountRatio);

        /// <summary>El saldo se mueve el día que el banco liquida, no el de la compra.</summary>
        private static DateOnly SettledOn(ImportRow row) => row.SettledDate ?? row.ValueDate;

        /// <summary>
        /// Un pago que el banco aún tiene «pendiente» ya tiene el dinero apartado: entra como pagado,
        /// aunque la fila siga guardando que el banco no lo ha completado.
        /// </summary>
        internal static bool EntersAsPaid(ImportRow row) =>
            row.ClassifiedStatus is EntryStatus.Paid or EntryStatus.Pending;

        private static string CreateFeeFingerprint(string rowFingerprint)
        {
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes($"{rowFingerprint}|fee"));
            return Convert.ToHexString(bytes);
        }
    }
}
