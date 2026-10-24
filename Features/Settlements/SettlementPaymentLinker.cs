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
    /// booked in her open settlements: money in pays what she owes me, money out pays what I
    /// owe her. The oldest settlement is paid first and the newest one keeps any excess. A
    /// sent settlement that reaches 0 is marked as paid (closed).
    /// </summary>
    public static class SettlementPaymentLinker
    {
        private const int MinBankNameLength = 4;

        /// <returns>How many bank movements were booked in a settlement.</returns>
        public static async Task<int> LinkAsync(
            IAppDbContext db,
            Guid userId,
            IReadOnlyCollection<SettlementPaymentCandidate> candidates,
            DateTime now,
            CancellationToken ct)
        {
            if (candidates.Count == 0) return 0;

            var people = (await db.Counterparties
                    .AsNoTracking()
                    .Where(person => person.UserId == userId && person.BankName != null)
                    .Select(person => new { person.Id, person.BankName })
                    .ToListAsync(ct))
                .Select(person => (person.Id, BankName: Normalize(person.BankName)))
                .Where(person => person.BankName.Length >= MinBankNameLength)
                .OrderByDescending(person => person.BankName.Length)
                .ToList();
            if (people.Count == 0) return 0;

            var personIds = people.Select(person => person.Id).ToList();
            var settlements = await db.Settlements
                .Include(settlement => settlement.Lines)
                .Where(settlement => settlement.UserId == userId && personIds.Contains(settlement.CounterpartyId))
                .ToListAsync(ct);
            if (settlements.Count == 0) return 0;

            var entryIds = candidates.Select(candidate => candidate.LedgerEntryId).ToList();
            var used = (await db.SettlementLines
                    .Where(line => line.LedgerEntryId != null && entryIds.Contains(line.LedgerEntryId.Value))
                    .Select(line => line.LedgerEntryId!.Value)
                    .ToListAsync(ct))
                .ToHashSet();

            var linked = 0;
            foreach (var candidate in candidates.OrderBy(candidate => candidate.Date))
            {
                if (candidate.SignedAmount == 0m || !used.Add(candidate.LedgerEntryId)) continue;

                var text = $" {Normalize(candidate.BankText)} ";
                var personId = people
                    .Where(person => text.Contains($" {person.BankName} ", StringComparison.Ordinal))
                    .Select(person => (Guid?)person.Id)
                    .FirstOrDefault();
                if (personId is null) continue;

                var isIncoming = candidate.SignedAmount > 0m;
                var targets = SettlementForecast
                    .Open(settlements.Where(settlement => settlement.CounterpartyId == personId.Value))
                    .Where(settlement => settlement.PeriodStart <= candidate.Date)
                    .Where(settlement => isIncoming
                        ? SettlementForecast.Remaining(settlement) > 0m
                        : SettlementForecast.Remaining(settlement) < 0m)
                    .ToList();
                if (targets.Count == 0) continue;

                var full = Math.Abs(candidate.SignedAmount);
                var left = full;
                for (var index = 0; index < targets.Count && left > 0m; index++)
                {
                    var settlement = targets[index];
                    var take = index == targets.Count - 1
                        ? left
                        : Math.Min(left, Math.Abs(SettlementForecast.Remaining(settlement)));
                    AddLine(db, settlement, isIncoming, take, full, candidate, now);
                    left -= take;

                    if (settlement.Status == SettlementStatus.Sent
                        && Math.Abs(SettlementForecast.Remaining(settlement)) < 0.01m)
                    {
                        settlement.Status = SettlementStatus.Closed;
                    }
                    settlement.UpdatedAt = now;
                }

                linked++;
            }

            return linked;
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
