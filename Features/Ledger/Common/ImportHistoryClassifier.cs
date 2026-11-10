using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Reuses how the user already classified the same merchant in earlier imports, so
    /// repeated movements never reach the AI. The final category of the booked entry wins
    /// over the one picked during the import, because the user may have fixed it later.
    /// </summary>
    public static class ImportHistoryClassifier
    {
        public const string Source = "historial";
        private const int MaxHistoryRows = 5000;
        private const decimal MinShare = 0.6m;

        public static string Key(string? description, decimal amount)
        {
            if (string.IsNullOrWhiteSpace(description)) return string.Empty;
            var builder = new StringBuilder(amount < 0m ? "-" : "+");
            var pendingSpace = false;
            foreach (var character in description.Normalize(NormalizationForm.FormD))
            {
                if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetter(character))
                {
                    if (pendingSpace && builder.Length > 1) builder.Append(' ');
                    builder.Append(char.ToUpperInvariant(character));
                    pendingSpace = false;
                }
                else
                {
                    pendingSpace = true;
                }
            }

            return builder.Length > 1 ? builder.ToString() : string.Empty;
        }

        public static async Task<IReadOnlyDictionary<string, Guid>> LoadAsync(
            IAppDbContext db, Guid userId, CancellationToken ct)
        {
            var history = await (
                    from row in db.ImportRows.AsNoTracking()
                    where row.UserId == userId
                        && (row.LedgerEntryId != null || row.ConfirmedConceptId != null)
                    join entry in db.LedgerEntries.AsNoTracking() on row.LedgerEntryId equals entry.Id into entries
                    from entry in entries.DefaultIfEmpty()
                    orderby row.ValueDate descending
                    select new
                    {
                        row.NormalizedDescription,
                        row.RawDescription,
                        row.Amount,
                        ConceptId = entry != null ? (Guid?)entry.ConceptId : row.ConfirmedConceptId
                    })
                .Take(MaxHistoryRows)
                .ToListAsync(ct);

            var activeConcepts = (await db.Concepts.AsNoTracking()
                    .Where(concept => concept.UserId == userId && concept.IsActive)
                    .Select(concept => concept.Id)
                    .ToListAsync(ct))
                .ToHashSet();

            return history
                .Where(item => item.ConceptId is { } id && activeConcepts.Contains(id))
                .GroupBy(item => Key(item.NormalizedDescription ?? item.RawDescription, item.Amount))
                .Where(group => group.Key.Length > 0)
                .Select(group =>
                {
                    var total = group.Count();
                    var best = group.GroupBy(item => item.ConceptId!.Value)
                        .OrderByDescending(byConcept => byConcept.Count())
                        .First();
                    return (group.Key, ConceptId: best.Key, Share: (decimal)best.Count() / total);
                })
                .Where(item => item.Share >= MinShare)
                .ToDictionary(item => item.Key, item => item.ConceptId);
        }
    }
}
