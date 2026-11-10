using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Reminders;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ImportObligationValidation(
        IReadOnlyDictionary<Guid, Reminder> Reminders,
        IReadOnlyDictionary<Guid, Settlement> Settlements,
        string? Error);

    public static class ImportObligationLinks
    {
        public static bool HasLink(ImportRow row) => row.ReminderId is not null || row.SettlementId is not null;

        public static string? RowError(ImportRow row) =>
            row.ReminderId is not null && row.SettlementId is not null
                ? "Elige un recordatorio o una liquidación, no ambos."
            : row.Amount == 0m || !ImportBatchApplicationService.EntersAsPaid(row)
                ? "Solo se puede vincular un movimiento bancario con importe."
            : ImportPlannedLinks.HasLink(row) || row.Allocations.Count > 0 || row.PasanacoParticipantId is not null
                ? "Quita primero el previsto, reparto o cuota del movimiento antes de vincular otra deuda."
            : null;

        public static string? AmountError(decimal signedAmount, bool isIncome, decimal remaining, bool repeating = false) =>
            signedAmount == 0m || (signedAmount > 0m) != isIncome
                ? "El cobro o pago no tiene la misma dirección que lo pendiente."
            : remaining <= 0m || Math.Abs(signedAmount) > remaining
                ? "El importe supera lo pendiente. Revisa el vínculo antes de aplicar."
            : repeating && Math.Abs(signedAmount) != remaining
                ? "Un recordatorio repetitivo debe cubrirse completo; no se puede reducir el importe de sus próximos meses."
            : null;

        public static async Task<Dictionary<Guid, Settlement>> LoadOpenSettlementsAsync(
            IAppDbContext db, Guid userId, CancellationToken ct)
        {
            var all = await db.Settlements
                .Include(settlement => settlement.Lines)
                .Where(settlement => settlement.UserId == userId)
                .ToListAsync(ct);
            var activePeople = (await db.Counterparties.AsNoTracking()
                .Where(person => person.UserId == userId && person.IsActive)
                .Select(person => person.Id).ToListAsync(ct)).ToHashSet();
            return all.GroupBy(settlement => settlement.CounterpartyId)
                .SelectMany(group => SettlementForecast.Open(group))
                .Where(settlement => activePeople.Contains(settlement.CounterpartyId))
                .ToDictionary(settlement => settlement.Id);
        }

        public static async Task<ImportObligationValidation> ValidateAsync(
            IAppDbContext db, Guid userId, IEnumerable<ImportRow> rows, CancellationToken ct, bool includeBooked = false)
        {
            var linked = rows.Where(row => (row.Status == ImportRowStatus.Pending ||
                includeBooked && row.Status == ImportRowStatus.Accepted) && HasLink(row)).ToList();
            var reminderIds = linked.Where(row => row.ReminderId.HasValue)
                .Select(row => row.ReminderId!.Value).ToHashSet();
            var reminders = reminderIds.Count == 0
                ? new Dictionary<Guid, Reminder>()
                : await db.Reminders.Where(reminder => reminder.UserId == userId && reminderIds.Contains(reminder.Id))
                    .ToDictionaryAsync(reminder => reminder.Id, ct);
            var settlements = linked.Any(row => row.SettlementId.HasValue)
                ? await LoadOpenSettlementsAsync(db, userId, ct)
                : new Dictionary<Guid, Settlement>();
            string? error = linked.Select(RowError).FirstOrDefault(message => message is not null);
            if (error is not null) return new(reminders, settlements, error);
            foreach (var group in linked.Where(row => row.ReminderId.HasValue).GroupBy(row => row.ReminderId!.Value))
            {
                if (!reminders.TryGetValue(group.Key, out var reminder) || reminder.IsDone || reminder.Amount is not > 0m)
                    return new(reminders, settlements, "El recordatorio ya no está pendiente o no tiene importe.");
                if (group.Any(row => row.ReminderDueDate != reminder.DueDate))
                    return new(reminders, settlements, "La fecha del recordatorio cambió. Quita el vínculo y vuelve a confirmarlo.");
                if (group.Any(row => (row.Amount > 0m) != reminder.IsIncome))
                    return new(reminders, settlements, "El recordatorio y el movimiento no tienen la misma dirección.");
                var total = group.Sum(row => Math.Abs(row.Amount));
                error = AmountError(reminder.IsIncome ? total : -total, reminder.IsIncome,
                    reminder.Amount.Value, reminder.Repeat != ReminderRepeat.None);
                if (error is not null) return new(reminders, settlements, error);
            }
            foreach (var group in linked.Where(row => row.SettlementId.HasValue).GroupBy(row => row.SettlementId!.Value))
            {
                if (!settlements.TryGetValue(group.Key, out var settlement))
                    return new(reminders, settlements, "La liquidación ya no está pendiente o su saldo pasó a otra.");
                var remaining = SettlementForecast.Remaining(settlement);
                if (group.Any(row => (row.Amount > 0m) != (remaining > 0m)))
                    return new(reminders, settlements, "La liquidación y el movimiento no tienen la misma dirección.");
                error = AmountError(group.Sum(row => row.Amount), remaining > 0m, Math.Abs(remaining));
                if (error is not null) return new(reminders, settlements, error);
            }
            return new(reminders, settlements, null);
        }

        public static async Task<Dictionary<Guid, Reminder>> FindAutomaticRemindersAsync(
            IAppDbContext db, Guid userId, IReadOnlyCollection<ImportRow> rows, CancellationToken ct)
        {
            var result = new Dictionary<Guid, Reminder>();
            var reserved = rows.Where(row => row.Status == ImportRowStatus.Pending && row.ReminderId.HasValue)
                .Select(row => row.ReminderId!.Value).ToHashSet();
            var conceptIds = (await db.Concepts.AsNoTracking().Where(concept =>
                concept.UserId == userId && concept.IsActive && concept.Kind != ConceptKind.Transfer)
                .Select(concept => concept.Id).ToListAsync(ct)).ToHashSet();
            var candidates = rows.Where(row => row.Status == ImportRowStatus.Pending && !HasLink(row) &&
                RowError(row) is null && (row.ConfirmedConceptId ?? row.SuggestedConceptId) is { } id &&
                conceptIds.Contains(id)).ToList();
            if (candidates.Count == 0) return result;
            var fingerprints = candidates.Select(row => row.Fingerprint).ToList();
            var existing = (await db.LedgerEntries.AsNoTracking()
                .Where(entry => entry.UserId == userId && entry.Fingerprint != null && fingerprints.Contains(entry.Fingerprint))
                .Select(entry => entry.Fingerprint!).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
            candidates = candidates.Where(row => !existing.Contains(row.Fingerprint)).ToList();
            var reminders = await db.Reminders.Where(reminder => reminder.UserId == userId &&
                !reminder.IsDone && reminder.Amount > 0m && !reserved.Contains(reminder.Id)).ToListAsync(ct);
            if (reminders.Count == 0 || candidates.Count == 0) return result;
            var forecasts = await db.LedgerEntries.AsNoTracking().Include(entry => entry.Concept)
                .Where(ImportPlannedLinks.IsOpenExpression)
                .Where(entry => entry.UserId == userId).ToListAsync(ct);
            var settlementRows = await SettlementPaymentLinker.FindCandidateIdsAsync(db, userId,
                candidates.Select(row => new SettlementPaymentCandidate(row.Id, row.RawDescription, row.Amount, row.ValueDate)).ToList(), ct);
            foreach (var row in candidates)
            {
                if (settlementRows.Contains(row.Id)) continue;
                var matching = reminders.Where(reminder => reminder.IsIncome == (row.Amount > 0m) &&
                    reminder.DueDate <= row.ValueDate.AddDays(ImportPlannedLinks.CandidateWindowDays) &&
                    reminder.Amount == Math.Abs(row.Amount) &&
                    ImportPendingRelevance.HasIdentifier(row.RawDescription, reminder.Title)).ToList();
                if (matching.Count != 1 || forecasts.Any(entry =>
                    entry.Direction == ImportPlannedLinks.DirectionOf(row.Amount) &&
                    entry.ForecastAmount == Math.Abs(row.Amount) &&
                    ImportPendingRelevance.HasIdentifier(row.RawDescription, $"{entry.Description} {entry.Concept?.Name}")))
                    continue;
                result[row.Id] = matching[0];
            }
            var ambiguousIds = result.Values.GroupBy(reminder => reminder.Id)
                .Where(group => group.Count() > 1).Select(group => group.Key).ToHashSet();
            return result.Where(pair => !ambiguousIds.Contains(pair.Value.Id)).ToDictionary();
        }

        public static async Task<int> ApplyAsync(
            IAppDbContext db, Guid userId, IReadOnlyCollection<ImportRow> booked,
            ImportObligationValidation validation, DateTime now, CancellationToken ct)
        {
            foreach (var group in booked.Where(row => row.ReminderId.HasValue).GroupBy(row => row.ReminderId!.Value))
            {
                var reminder = validation.Reminders[group.Key];
                var total = group.Sum(row => Math.Abs(row.Amount));
                if (total == reminder.Amount)
                    ReminderRules.Complete(reminder, now);
                else
                {
                    reminder.Amount -= total;
                    reminder.UpdatedAt = now;
                }
            }
            var settlementPayments = 0;
            foreach (var group in booked.Where(row => row.SettlementId.HasValue).GroupBy(row => row.SettlementId!.Value))
            {
                var settlement = validation.Settlements[group.Key];
                foreach (var row in group)
                {
                    var entry = db.LedgerEntries.Local.Single(entry => entry.Id == row.LedgerEntryId);
                    if (settlement.IsExpenseReimbursement)
                        await SharedExpenseImport.ClassifyPaymentAsync(db, userId, settlement, entry, ct);
                    SettlementPaymentLinker.ApplyPayment(db, settlement,
                        new(entry.Id, row.RawDescription, row.Amount, row.ValueDate), now);
                    settlementPayments++;
                }
                await SettlementEntryCoverage.RefreshAsync(db, settlement, ct);
            }
            return settlementPayments;
        }
    }
}
