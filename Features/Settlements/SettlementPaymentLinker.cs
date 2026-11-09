using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Settlements
{
    /// <summary>A paid bank movement that may be money settled with a person.</summary>
    public sealed record SettlementPaymentCandidate(
        Guid LedgerEntryId,
        string BankText,
        decimal SignedAmount,
        DateOnly Date);

    /// <summary>
    /// When a bank file is applied, a movement whose text contains a person's bank name is
    /// booked against a single open settlement with the exact signed remaining amount.
    /// Ambiguous matches and differing amounts are left for explicit review.
    /// </summary>
    public static class SettlementPaymentLinker
    {
        private const int MinBankNameLength = 4;

        public static async Task<HashSet<Guid>> FindCandidateIdsAsync(
            IAppDbContext db,
            Guid userId,
            IReadOnlyCollection<SettlementPaymentCandidate> candidates,
            CancellationToken ct)
        {
            var targets = await FindTargetsAsync(db, userId, candidates, ct);
            return targets.Keys.ToHashSet();
        }

        /// <returns>How many bank movements were booked in a settlement.</returns>
        public static async Task<int> LinkAsync(
            IAppDbContext db,
            Guid userId,
            IReadOnlyCollection<SettlementPaymentCandidate> candidates,
            DateTime now,
            CancellationToken ct)
        {
            if (candidates.Count == 0) return 0;

            var targetsByCandidate = await FindTargetsAsync(db, userId, candidates, ct);
            var entryIds = candidates.Select(candidate => candidate.LedgerEntryId).ToList();
            var used = (await db.SettlementLines
                    .Where(line => line.Settlement!.UserId == userId
                        && line.LedgerEntryId != null && entryIds.Contains(line.LedgerEntryId.Value))
                    .Select(line => line.LedgerEntryId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();

            var linked = 0;
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Date))
            {
                if (!used.Add(candidate.LedgerEntryId)
                    || !targetsByCandidate.TryGetValue(candidate.LedgerEntryId, out var targets))
                    continue;

                var matching = targets
                    .Where(settlement => settlement.Status != SettlementStatus.Closed
                        && SettlementForecast.Remaining(settlement) == candidate.SignedAmount)
                    .ToList();
                if (matching.Count != 1) continue;

                var settlement = matching[0];
                if (settlement.IsExpenseReimbursement)
                {
                    var entry = db.LedgerEntries.Local.FirstOrDefault(item => item.Id == candidate.LedgerEntryId)
                        ?? await db.LedgerEntries.FirstAsync(item =>
                            item.Id == candidate.LedgerEntryId && item.UserId == userId, ct);
                    await SharedExpenseImport.ClassifyPaymentAsync(db, userId, settlement, entry, ct);
                }
                ApplyPayment(db, settlement, candidate, now);
                await SettlementEntryCoverage.RefreshAsync(db, settlement, ct);
                linked++;
            }

            return linked;
        }

        private static async Task<Dictionary<Guid, List<Settlement>>> FindTargetsAsync(
            IAppDbContext db,
            Guid userId,
            IReadOnlyCollection<SettlementPaymentCandidate> candidates,
            CancellationToken ct)
        {
            var result = new Dictionary<Guid, List<Settlement>>();
            if (candidates.Count == 0) return result;
            var people = (await db.Counterparties
                    .AsNoTracking()
                    .Where(person => person.UserId == userId && person.IsActive && person.BankName != null)
                    .Select(person => new { person.Id, person.BankName })
                    .ToListAsync(ct))
                .Select(person => (person.Id, BankName: Normalize(person.BankName)))
                .Where(person => person.BankName.Length >= MinBankNameLength)
                .OrderByDescending(person => person.BankName.Length)
                .ToList();
            if (people.Count == 0) return result;

            var personIds = people.Select(person => person.Id).ToList();
            var settlements = await db.Settlements
                .Include(settlement => settlement.Lines)
                .Where(settlement => settlement.UserId == userId && personIds.Contains(settlement.CounterpartyId))
                .ToListAsync(ct);
            settlements = settlements.Concat(db.Settlements.Local.Where(settlement =>
                settlement.UserId == userId && personIds.Contains(settlement.CounterpartyId)))
                .DistinctBy(settlement => settlement.Id).ToList();
            if (settlements.Count == 0) return result;

            foreach (var candidate in candidates.OrderBy(candidate => candidate.Date))
            {
                if (candidate.SignedAmount == 0m) continue;

                var text = $" {Normalize(candidate.BankText)} ";
                var matchingPeople = people
                    .Where(person => text.Contains($" {person.BankName} ", StringComparison.Ordinal))
                    .ToList();
                if (matchingPeople.Count != 1) continue;

                var isIncoming = candidate.SignedAmount > 0m;
                var targets = SettlementForecast
                    .Open(settlements.Where(settlement => settlement.CounterpartyId == matchingPeople[0].Id))
                    .Where(settlement => settlement.PeriodStart <= candidate.Date)
                    .Where(settlement => isIncoming
                        ? SettlementForecast.Remaining(settlement) > 0m
                        : SettlementForecast.Remaining(settlement) < 0m)
                    .ToList();
                if (targets.Count == 0) continue;
                result[candidate.LedgerEntryId] = targets;
            }

            return result;
        }

        private static void AddLine(
            IAppDbContext db,
            Settlement settlement,
            bool isIncoming,
            decimal amount,
            decimal full,
            SettlementPaymentCandidate candidate,
            DateTime now)
        {
            var kind = isIncoming ? SettlementLineKind.Payment : SettlementLineKind.PaymentSent;
            var day = candidate.Date.ToString("dd/MM", CultureInfo.InvariantCulture);
            var line = new SettlementLine
            {
                SettlementId = settlement.Id,
                Kind = kind,
                Description = isIncoming ? $"Transferencia recibida {day}" : $"Transferencia enviada {day}",
                Amount = amount,
                FullAmount = amount == full ? null : full,
                LedgerEntryId = candidate.LedgerEntryId,
                SortOrder = (int)kind * 1000 + settlement.Lines.Count(existing => existing.Kind == kind),
                CreatedAt = now
            };

            settlement.Lines.Add(line);
            db.SettlementLines.Add(line);
        }

        public static void ApplyPayment(
            IAppDbContext db,
            Settlement settlement,
            SettlementPaymentCandidate candidate,
            DateTime now)
        {
            var amount = Math.Abs(candidate.SignedAmount);
            AddLine(db, settlement, candidate.SignedAmount > 0m, amount, amount, candidate, now);
            if (SettlementForecast.Remaining(settlement) == 0m)
                settlement.Status = SettlementStatus.Closed;
            settlement.UpdatedAt = now;
        }

        /// <summary>Upper case, no accents, only letters and digits split by single spaces.</summary>
        public static string Normalize(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return string.Empty;

            var builder = new StringBuilder(value.Length);
            var pendingSpace = false;
            foreach (var character in value.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(character))
                {
                    if (pendingSpace && builder.Length > 0) builder.Append(' ');
                    builder.Append(char.ToUpperInvariant(character));
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = true;
                }
            }

            return builder.ToString();
        }
    }
}
