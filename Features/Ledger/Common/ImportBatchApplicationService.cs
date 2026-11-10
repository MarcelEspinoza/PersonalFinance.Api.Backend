using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Settlements;
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
        int MatchedForecasts = 0,
        int SettlementPayments = 0);

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
                .Include(item => item.Rows)
                    .ThenInclude(row => row.PlannedAllocations)
                .FirstOrDefaultAsync(
                    item => item.Id == batchId && item.UserId == userId,
                    ct);
            if (batch is null)
                return new(false, 0, batchId, NotFound: true);
            if (batch.Status == ImportBatchStatus.Applied)
                return new(false, 0, batchId, "El lote ya se ha aplicado.", Conflict: true);
            if (batch.Status == ImportBatchStatus.Discarded)
                return new(false, 0, batchId, "El lote está descartado.", Conflict: true);
            if (batch.Rows.Count == 0)
                return new(false, 0, batchId, "El lote no contiene movimientos para aplicar.");

            // Rows the user deducts from a planned entry: each one is booked as its own movement
            // and the planned entry is reduced (or closed) instead of being auto-matched.
            var linkedRows = batch.Rows.Where(ImportPlannedLinks.Deducts).ToList();
            var linkedEntryIds = linkedRows.SelectMany(ImportPlannedLinks.Links)
                .Select(link => link.PlannedEntryId).ToHashSet();
            var plannedById = linkedEntryIds.Count == 0
                ? new Dictionary<Guid, LedgerEntry>()
                : await _db.LedgerEntries
                    .Include(entry => entry.Period)
                    .Include(entry => entry.Concept)
                    .Where(entry => entry.UserId == userId && linkedEntryIds.Contains(entry.Id))
                    .ToDictionaryAsync(entry => entry.Id, ct);
            var deductedRows = linkedRows
                .Where(row => ImportPlannedLinks.IsValidLinks(row, plannedById))
                .ToList();
            foreach (var row in batch.Rows.Where(row => row.Status == ImportRowStatus.Pending && row.PlannedAllocations.Count > 0))
            {
                var error = ImportPlannedLinks.AllocationError(row, ImportPlannedLinks.Links(row).ToList(), plannedById);
                if (error is not null) return new(false, 0, batchId, error, Conflict: true);
                var conceptId = row.ConfirmedConceptId ?? row.SuggestedConceptId;
                if (conceptId is not null && conceptId != plannedById[row.PlannedAllocations.First().PlannedEntryId].ConceptId)
                    return new(false, 0, batchId, "La categoría del movimiento no coincide con la de los previstos del reparto.", Conflict: true);
                error = await ImportPlannedLinks.CareAllocationErrorAsync(
                    _db, userId, ImportPlannedLinks.Links(row).ToList(), plannedById, ct);
                if (error is not null) return new(false, 0, batchId, error, Conflict: true);
            }
            var deductedRowIds = deductedRows.Select(row => row.Id).ToHashSet();
            var deductedEntryIds = deductedRows.SelectMany(ImportPlannedLinks.Links)
                .Select(link => link.PlannedEntryId).ToHashSet();
            var legacyDeductedEntryIds = deductedRows.Where(row => row.PlannedAllocations.Count == 0)
                .SelectMany(ImportPlannedLinks.Links).Select(link => link.PlannedEntryId).ToHashSet();
            if (await _db.ZeusCareStays.AnyAsync(stay =>
                stay.UserId == userId && stay.LedgerEntryId.HasValue
                && legacyDeductedEntryIds.Contains(stay.LedgerEntryId.Value), ct))
                return new(false, 0, batchId,
                    "Una previsión está vinculada a cuidados de Zeus. No la reduzcas como un previsto genérico: deja que se empareje con el pago o importa el Bizum y vincúlalo desde «Zeus».",
                    Conflict: true);
            var explicitEntryIds = deductedRows.SelectMany(row => row.PlannedAllocations)
                .Select(link => link.PlannedEntryId).ToHashSet();
            foreach (var id in explicitEntryIds)
            {
                var total = deductedRows.SelectMany(ImportPlannedLinks.Links)
                    .Where(link => link.PlannedEntryId == id).Sum(link => link.Amount);
                if (total > plannedById[id].ForecastAmount + ImportPlannedLinks.CoverTolerance)
                    return new(false, 0, batchId, "El reparto supera lo pendiente de un previsto. Actualiza los importes antes de aplicar.", Conflict: true);
            }
            var allocatedCareStays = await _db.ZeusCareStays
                .Where(stay => stay.UserId == userId && stay.LedgerEntryId.HasValue &&
                    explicitEntryIds.Contains(stay.LedgerEntryId.Value))
                .ToListAsync(ct);
            foreach (var plannedId in deductedEntryIds)
            {
                var plannedPeriod = plannedById[plannedId].Period;
                if (plannedPeriod?.Status == PeriodStatus.Closed)
                    return new(
                        false,
                        0,
                        batchId,
                        $"El periodo {plannedPeriod.Year}-{plannedPeriod.Month:D2} de un previsto del que descuentas movimientos está cerrado.",
                        Conflict: true);
            }

            Guid? FallbackConcept(ImportRow row) =>
                row.Allocations.Count == 0 &&
                ImportPlannedLinks.Links(row).Select(link => (Guid?)link.PlannedEntryId).FirstOrDefault() is { } plannedId &&
                plannedById.TryGetValue(plannedId, out var planned)
                    ? planned.ConceptId
                    : null;

            var unassigned = batch.Rows.Count(row =>
                row.Status == ImportRowStatus.Pending &&
                row.Allocations.Count == 0 &&
                row.ConfirmedConceptId is null &&
                row.SuggestedConceptId is null &&
                FallbackConcept(row) is null);
            if (unassigned > 0)
                return new(false, 0, batchId, "Hay filas sin concepto asignado.", unassigned);

            var accountId = batch.AccountId;
            if (accountId is null)
                return new(false, 0, batchId, "El lote no tiene cuenta.");

            var concepts = await _db.Concepts
                .Where(concept => concept.UserId == userId)
                .ToDictionaryAsync(concept => concept.Id, ct);
            var sharedPersonIds = batch.Rows.Where(row => row.Status == ImportRowStatus.Pending).SelectMany(row => row.Allocations)
                .Where(part => part.CounterpartyId.HasValue).Select(part => part.CounterpartyId!.Value).ToHashSet();
            var activeSharedPeople = (await _db.Counterparties
                .Where(person => person.UserId == userId && person.IsActive && sharedPersonIds.Contains(person.Id))
                .Select(person => person.Id).ToListAsync(ct)).ToHashSet();
            if (!sharedPersonIds.SetEquals(activeSharedPeople))
                return new(false, 0, batchId, "Alguna persona del reparto ya no está disponible. Revisa el gasto compartido.", Conflict: true);
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
            var obligationValidation = await ImportObligationLinks.ValidateAsync(
                _db, userId, batch.Rows.Where(row => !existingFingerprints.Contains(row.Fingerprint)), ct);
            if (obligationValidation.Error is { } obligationError)
                return new(false, 0, batchId, obligationError, Conflict: true);
            if (batch.Rows.Any(row => row.Status == ImportRowStatus.Pending && ImportObligationLinks.HasLink(row)
                && (row.ConfirmedConceptId ?? row.SuggestedConceptId) is { } id && mirrorAccountByConcept.ContainsKey(id)))
                return new(false, 0, batchId, "Un traspaso entre tus propias cuentas no puede pagar una deuda.", Conflict: true);
            var automaticReminders = await ImportObligationLinks.FindAutomaticRemindersAsync(
                _db, userId, batch.Rows.ToList(), ct);
            var plannedMatches = await FindForecastMatchesAsync(
                userId,
                accountId.Value,
                batch.Rows.Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    EntersAsPaid(row) &&
                    row.Allocations.Count == 0 &&
                    !ImportObligationLinks.HasLink(row) &&
                    row.PasanacoParticipantId is null &&
                    !automaticReminders.ContainsKey(row.Id) &&
                    !deductedRowIds.Contains(row.Id) &&
                    !existingFingerprints.Contains(row.Fingerprint)),
                deductedEntryIds,
                ct);
            var matchByRowId = plannedMatches.ToDictionary(match => match.Row.Id);
            var matchedCareEntryIds = plannedMatches.Where(match => match.Entry is not null)
                .Select(match => match.Entry!.Id).ToList();
            var careAmounts = await _db.ZeusCareStays.Where(stay => stay.UserId == userId
                    && stay.LedgerEntryId.HasValue && matchedCareEntryIds.Contains(stay.LedgerEntryId.Value))
                .GroupBy(stay => stay.LedgerEntryId!.Value)
                .Select(group => new { Id = group.Key, Amount = group.Sum(stay => stay.Amount) })
                .ToDictionaryAsync(item => item.Id, item => item.Amount, ct);
            if (plannedMatches.Any(match => match.Entry is not null
                && careAmounts.GetValueOrDefault(match.Entry.Id) > Math.Abs(match.Row.Amount)))
                return new(false, 0, batchId,
                    "Un pago no cubre todos los cuidados de Zeus vinculados a su previsión. Revisa sus precios o importa el movimiento por separado y vincúlalo desde «Zeus».",
                    Conflict: true);
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
                        entry.Status != EntryStatus.Skipped &&
                        !deductedEntryIds.Contains(entry.Id),
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
            var bookedDeductions = new List<(Guid PlannedEntryId, decimal Amount)>();
            var settlementCandidates = new List<SettlementPaymentCandidate>();
            var pasanacoRows = new List<ImportRow>();
            var bookedObligations = new List<ImportRow>();
            foreach (var row in batch.Rows.Where(row => row.Status == ImportRowStatus.Pending))
            {
                var isSplit = row.Allocations.Count > 0;
                var conceptId = isSplit ? null : row.ConfirmedConceptId ?? row.SuggestedConceptId ?? FallbackConcept(row);
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
                if (automaticReminders.TryGetValue(row.Id, out var automaticReminder))
                {
                    row.ReminderId = automaticReminder.Id;
                    row.ReminderDueDate = automaticReminder.DueDate;
                }
                if (isSplit)
                {
                    var sharedParts = row.Allocations.Where(part => part.CounterpartyId.HasValue).ToList();
                    if (sharedParts.Count > 0 && (sharedParts.Count != 1 || row.Allocations.Count != 2 ||
                        row.Amount >= 0m || !isPaid || !concepts.TryGetValue(sharedParts[0].ConceptId, out var advanceConcept) ||
                        advanceConcept.Kind != ConceptKind.Transfer || advanceConcept.Name != SharedExpenseImport.ConceptName ||
                        mirrorAccountByConcept.ContainsKey(advanceConcept.Id)))
                        return new(false, 0, batchId, "El reparto compartido ya no es válido. Vuelve a revisarlo.", Conflict: true);
                    if (row.Allocations.Count is < 2 or > 3 ||
                        row.Allocations.Select(allocation => allocation.ConceptId).Distinct().Count() != row.Allocations.Count ||
                        row.Allocations.Sum(allocation => allocation.Amount) != amount ||
                        row.Allocations.Any(allocation =>
                            allocation.Amount <= 0m ||
                            decimal.Round(allocation.Amount, 2) != allocation.Amount ||
                            !concepts.TryGetValue(allocation.ConceptId, out var allocationConcept) ||
                            !allocationConcept.IsActive ||
                            allocationConcept.Kind != (allocation.CounterpartyId.HasValue
                                ? ConceptKind.Transfer : row.Amount > 0m ? ConceptKind.Income : ConceptKind.Expense)))
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
                            IsTransfer = concepts[allocation.ConceptId].Kind == ConceptKind.Transfer,
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
                        if (allocation.CounterpartyId is { } personId)
                        {
                            allocationEntry.Notes = "Parte adelantada por un gasto compartido; pendiente de devolución.";
                            await SharedExpenseImport.AddAdvanceAsync(_db, userId, personId, allocationEntry, amount, DateTime.UtcNow, ct);
                        }
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
                    if (row.PasanacoParticipantId is not null && row.Amount > 0m) pasanacoRows.Add(row);
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
                    if (!forecastEntry.IsTransfer)
                        settlementCandidates.Add(new(forecastEntry.Id, row.RawDescription, row.Amount, row.ValueDate));

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
                    if (isPaid && deductedRowIds.Contains(row.Id))
                    {
                        var links = ImportPlannedLinks.Links(row).ToList();
                        importedEntry.Notes = row.PlannedAllocations.Count == 0
                            ? $"Descontado del previsto «{PlannedName(plannedById[links[0].PlannedEntryId], concepts)}»."
                            : ImportPlannedLinks.AppendNote(null, "Repartido entre " + string.Join("; ", links.Select(link =>
                                $"«{PlannedName(plannedById[link.PlannedEntryId], concepts)}» ({plannedById[link.PlannedEntryId].DueDate:dd/MM/yyyy}): {ImportPlannedLinks.Money(link.Amount)}")) + ".");
                        bookedDeductions.AddRange(links);
                        foreach (var link in links)
                        {
                            foreach (var stay in allocatedCareStays.Where(stay => stay.LedgerEntryId == link.PlannedEntryId))
                            {
                                stay.LedgerEntryId = importedEntry.Id;
                                stay.LedgerEntry = importedEntry;
                            }
                        }
                    }
                    _db.LedgerEntries.Add(importedEntry);
                    row.LedgerEntryId = importedEntry.Id;
                    AddTransferMirror(importedEntry, mirrorAccountByConcept);
                    if (isPaid && !ImportObligationLinks.HasLink(row) && !deductedRowIds.Contains(row.Id) &&
                        row.PasanacoParticipantId is null &&
                        (!importedEntry.IsTransfer || concept.Name == SharedExpenseImport.ConceptName))
                        settlementCandidates.Add(new(importedEntry.Id, row.RawDescription, row.Amount, row.ValueDate));

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
                if (ImportObligationLinks.HasLink(row)) bookedObligations.Add(row);
                if (row.PasanacoParticipantId is not null && row.Amount > 0m) pasanacoRows.Add(row);
                applied++;
            }

            await PasanacoImportQuotas.MarkPaidAsync(_db, userId, pasanacoRows, ct);

            // A covered planned entry is skipped (not paid): the rows already carry the real
            // amounts, so the month counts them once and nothing stays pending.
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            foreach (var deduction in bookedDeductions.GroupBy(item => item.PlannedEntryId))
            {
                var planned = plannedById[deduction.Key];
                var total = deduction.Sum(item => item.Amount);
                var count = deduction.Count();
                if (total < planned.ForecastAmount - ImportPlannedLinks.CoverTolerance)
                {
                    planned.ForecastAmount -= total;
                    planned.Notes = ImportPlannedLinks.AppendNote(
                        planned.Notes,
                        $"Descontados {ImportPlannedLinks.Money(total)} de {ImportPlannedLinks.Movements(count)} " +
                        $"el {today.ToString("dd/MM/yyyy", System.Globalization.CultureInfo.InvariantCulture)}.");
                }
                else
                {
                    planned.Status = EntryStatus.Skipped;
                    planned.ActualAmount = null;
                    planned.ValueDate = null;
                    planned.Notes = ImportPlannedLinks.AppendNote(
                        planned.Notes,
                        $"Cubierto por {ImportPlannedLinks.Movements(count)} ({ImportPlannedLinks.Money(total)}).");
                }

                planned.UpdatedAt = DateTime.UtcNow;
            }

            obligationValidation = await ImportObligationLinks.ValidateAsync(
                _db, userId, bookedObligations, ct, includeBooked: true);
            if (obligationValidation.Error is { } finalObligationError)
                return new(false, 0, batchId, finalObligationError, Conflict: true);
            var settlementPayments = await ImportObligationLinks.ApplyAsync(
                _db, userId, bookedObligations, obligationValidation, DateTime.UtcNow, ct);
            // Transfers to or from a person with open settlements are booked there automatically.
            settlementPayments += await SettlementPaymentLinker.LinkAsync(
                _db, userId, settlementCandidates, DateTime.UtcNow, ct);

            batch.AcceptedRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Accepted);
            batch.DuplicateRows = batch.Rows.Count(row => row.Status == ImportRowStatus.Duplicate);
            batch.Status = ImportBatchStatus.Applied;
            batch.AppliedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);

            return new(
                true,
                applied,
                batch.Id,
                MatchedForecasts: matchedForecasts,
                SettlementPayments: settlementPayments);
        }

        public async Task<IReadOnlyList<ImportForecastPreview>> PreviewForecastMatchesAsync(
            Guid userId, ImportBatch batch, CancellationToken ct)
        {
            ArgumentNullException.ThrowIfNull(batch);
            if (batch.Status is ImportBatchStatus.Applied or ImportBatchStatus.Discarded || batch.AccountId is null)
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
            var (deductedRowIds, deductedEntryIds) = await FindValidLinksAsync(userId, batch, ct);
            var matches = await FindForecastMatchesAsync(
                userId,
                batch.AccountId.Value,
                batch.Rows.Where(row =>
                    row.Status == ImportRowStatus.Pending &&
                    EntersAsPaid(row) &&
                    row.Allocations.Count == 0 &&
                    !ImportObligationLinks.HasLink(row) &&
                    row.PasanacoParticipantId is null &&
                    !deductedRowIds.Contains(row.Id) &&
                    !existingFingerprints.Contains(row.Fingerprint)),
                deductedEntryIds,
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

        /// <summary>Rows with a still-valid planned link and the planned entries they deduct from.</summary>
        private async Task<(HashSet<Guid> RowIds, HashSet<Guid> EntryIds)> FindValidLinksAsync(
            Guid userId, ImportBatch batch, CancellationToken ct)
        {
            var linkedRows = batch.Rows.Where(ImportPlannedLinks.Deducts).ToList();
            if (linkedRows.Count == 0) return (new HashSet<Guid>(), new HashSet<Guid>());

            var ids = linkedRows.SelectMany(ImportPlannedLinks.Links)
                .Select(link => link.PlannedEntryId).Distinct().ToList();
            var entries = await _db.LedgerEntries
                .AsNoTracking()
                .Include(entry => entry.Concept)
                .Include(entry => entry.Period)
                .Where(entry => entry.UserId == userId && ids.Contains(entry.Id))
                .ToDictionaryAsync(entry => entry.Id, ct);
            var valid = linkedRows
                .Where(row => ImportPlannedLinks.IsValidLinks(row, entries))
                .ToList();
            return (
                valid.Select(row => row.Id).ToHashSet(),
                valid.SelectMany(ImportPlannedLinks.Links).Select(link => link.PlannedEntryId).ToHashSet());
        }

        private static string PlannedName(LedgerEntry planned, IReadOnlyDictionary<Guid, Concept> concepts) =>
            concepts.TryGetValue(planned.ConceptId, out var concept)
                ? concept.Name
                : string.IsNullOrWhiteSpace(planned.Description) ? "previsto" : planned.Description.Trim();

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
            IReadOnlySet<Guid> excludedEntryIds,
            CancellationToken ct)
        {
            var candidates = rows
                .Where(row => row.ConfirmedConceptId.HasValue || row.SuggestedConceptId.HasValue)
                .Where(row => ImportPendingRelevance.HasMeaningfulText(row.RawDescription))
                .ToList();
            if (candidates.Count == 0) return new();
            var reminders = await _db.Reminders.AsNoTracking()
                .Where(reminder => reminder.UserId == userId && !reminder.IsDone && reminder.Amount > 0m)
                .Select(reminder => new { reminder.Title, reminder.Amount, reminder.IsIncome }).ToListAsync(ct);
            candidates = candidates.Where(row => !reminders.Any(reminder =>
                reminder.IsIncome == (row.Amount > 0m) && reminder.Amount == Math.Abs(row.Amount) &&
                ImportPendingRelevance.HasIdentifier(row.RawDescription, reminder.Title))).ToList();
            if (candidates.Count == 0) return new();
            var settlementRowIds = await SettlementPaymentLinker.FindCandidateIdsAsync(
                _db,
                userId,
                candidates.Select(row => new SettlementPaymentCandidate(
                    row.Id, row.RawDescription, row.Amount, row.ValueDate)).ToList(),
                ct);
            candidates = candidates.Where(row => !settlementRowIds.Contains(row.Id)).ToList();
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
                .Include(entry => entry.Concept)
                .Include(entry => entry.Period)
                .Where(entry =>
                    entry.UserId == userId &&
                    conceptIds.Contains(entry.ConceptId) &&
                    entry.Status != EntryStatus.Paid &&
                    entry.Status != EntryStatus.Skipped)
                .ToListAsync(ct);
            var availableEntries = existingEntries
                .Where(entry =>
                    entry.Status is not (EntryStatus.Paid or EntryStatus.Skipped) &&
                    entry.CashForecastAmount > 0m &&
                    !excludedEntryIds.Contains(entry.Id))
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
                        ((entry.AccountId == accountId &&
                          Math.Abs(entry.DueDate.DayNumber - row.ValueDate.DayNumber) <= ForecastMatchWindowDays &&
                          InAllowedMonth(entry.DueDate) &&
                          amount == entry.CashForecastAmount) ||
                         (ImportPlannedLinks.IsOpen(entry) &&
                          entry.Period?.Status != PeriodStatus.Closed &&
                          entry.Concept?.IsActive == true &&
                          entry.DueDate <= row.ValueDate.AddDays(ImportPlannedLinks.CandidateWindowDays) &&
                          amount == entry.CashForecastAmount &&
                          ImportPendingRelevance.HasIdentifier(row.RawDescription,
                              $"{entry.Description} {entry.Concept?.Name}"))) &&
                        (entry.SettlementAllocatedAmount == 0m || amount <= entry.CashForecastAmount))
                    .Select(entry => new ForecastMatch(
                        row, conceptId, direction, entry.DueDate, entry.CashForecastAmount, entry, null)));

                foreach (var rule in rules.Where(rule =>
                             rule.ConceptId == conceptId &&
                             rule.Direction == direction &&
                             rule.StartDate <= latest &&
                             (rule.EndDate is null || rule.EndDate >= earliest) &&
                             amount == rule.ForecastAmount))
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
                                 !excludedEntryIds.Contains(occurrence.Id) &&
                                 occurrence.ConceptId == conceptId &&
                                 occurrence.Direction == direction &&
                                 occurrence.CashForecastAmount > 0m &&
                                 amount == occurrence.CashForecastAmount &&
                                 (occurrence.SettlementAllocatedAmount == 0m || amount <= occurrence.CashForecastAmount) &&
                                 !options.Any(option => option.Row.Id == row.Id && option.Entry?.Id == occurrence.Id))
                        {
                            options.Add(new ForecastMatch(
                                row, conceptId, direction, occurrence.DueDate, occurrence.CashForecastAmount, occurrence, null));
                        }
                    }
                }
            }

            // Reparto uno a uno: gana la pareja con el importe más parecido y, si empatan,
            // la de fecha más cercana. Lo que no encaje se importa como movimiento nuevo.
            var matches = new List<ForecastMatch>();
            var matchedRows = new HashSet<Guid>();
            var usedForecasts = new HashSet<string>(StringComparer.Ordinal);
            var ambiguousRows = options.GroupBy(option => option.Row.Id)
                .Where(group =>
                {
                    var best = group.OrderBy(option => option.AmountGap).ThenBy(option => option.DayGap).First();
                    return group.Count(option => option.AmountGap == best.AmountGap && option.DayGap == best.DayGap) > 1;
                })
                .Select(group => group.Key).ToHashSet();
            var ambiguousForecasts = options.GroupBy(option => option.Key)
                .Where(group =>
                {
                    var best = group.OrderBy(option => option.AmountGap).ThenBy(option => option.DayGap).First();
                    return group.Count(option => option.AmountGap == best.AmountGap && option.DayGap == best.DayGap) > 1;
                })
                .Select(group => group.Key).ToHashSet(StringComparer.Ordinal);
            foreach (var option in options
                         .OrderBy(option => option.AmountGap)
                         .ThenBy(option => option.DayGap)
                         .ThenBy(option => option.DueDate)
                         .ThenBy(option => option.Row.RowNumber)
                         .ThenBy(option => option.Key, StringComparer.Ordinal))
            {
                if (ambiguousRows.Contains(option.Row.Id) || ambiguousForecasts.Contains(option.Key) ||
                    matchedRows.Contains(option.Row.Id) ||
                    !usedForecasts.Add(option.Key)) continue;
                matchedRows.Add(option.Row.Id);
                matches.Add(option);
            }

            return matches;
        }

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
