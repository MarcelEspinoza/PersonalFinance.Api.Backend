using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Features.Settlements
{
    /// <summary>Money an open settlement still expects: to collect (In) or to pay (Out).</summary>
    public sealed record SettlementExpectation(
        Guid SettlementId,
        Guid CounterpartyId,
        string Description,
        EntryDirection Direction,
        decimal Amount,
        DateOnly Date);

    /// <summary>How a settlement counts in the forecast, or why it does not (<see cref="Reason"/>).</summary>
    public sealed record SettlementForecastState(
        Guid SettlementId,
        SettlementExpectation? Expectation,
        string? Reason);

    /// <summary>
    /// The only place that decides what the settlements add to the month forecast.
    /// Drafts always count; sent ones count once they have an expected date; paid
    /// (closed) ones never. A settlement whose balance was carried over to a later
    /// one is already inside that later one, so it does not count again. The linked
    /// loan is left out: it is repaid on its own, not with the settlement.
    /// </summary>
    public static class SettlementForecast
    {
        public const int DefaultDaysToCollect = 7;
        public const string ConceptName = "Liquidación";

        /// <summary>What is still open without the linked loan. Positive: she pays me. Negative: I pay her.</summary>
        public static decimal Remaining(Settlement settlement) =>
            Math.Round(SettlementMessageBuilder.Totals(settlement).Pending, 2, MidpointRounding.AwayFromZero);

        public static DateOnly DefaultDate(Settlement settlement) =>
            settlement.PeriodEnd.AddDays(DefaultDaysToCollect);

        /// <summary>
        /// One person's settlements still waiting for money, oldest first: not paid, not
        /// carried over to a later one, and with something pending. Sent ones without an
        /// expected date are included: the money can arrive even if the forecast skips them.
        /// </summary>
        public static List<Settlement> Open(IEnumerable<Settlement> personSettlements)
        {
            var ordered = Ordered(personSettlements);
            var lastCarry = ordered.FindLastIndex(settlement => settlement.CarriedOverAmount != 0m);
            return ordered
                .Where((settlement, index) =>
                    settlement.Status != SettlementStatus.Closed
                    && index >= lastCarry
                    && Math.Abs(Remaining(settlement)) >= 0.01m)
                .ToList();
        }

        private static List<Settlement> Ordered(IEnumerable<Settlement> settlements) =>
            settlements
                .OrderBy(settlement => settlement.PeriodStart)
                .ThenBy(settlement => settlement.CreatedAt)
                .ToList();

        /// <param name="settlements">All of the user's settlements (closed ones too) with their lines.</param>
        public static IReadOnlyList<SettlementForecastState> Evaluate(
            IReadOnlyCollection<Settlement> settlements,
            IReadOnlyDictionary<Guid, string> names)
        {
            var result = new List<SettlementForecastState>(settlements.Count);
            foreach (var person in settlements.GroupBy(settlement => settlement.CounterpartyId))
            {
                var ordered = Ordered(person);
                var lastCarry = ordered.FindLastIndex(settlement => settlement.CarriedOverAmount != 0m);
                var name = names.TryGetValue(person.Key, out var found) && !string.IsNullOrWhiteSpace(found)
                    ? found.Trim()
                    : "Sin nombre";

                var counted = new List<(Settlement Settlement, decimal Remaining)>();
                foreach (var (settlement, index) in ordered.Select((settlement, index) => (settlement, index)))
                {
                    var remaining = Remaining(settlement);
                    var reason = settlement.Status == SettlementStatus.Closed
                        ? "Pagada"
                        : index < lastCarry
                            ? $"Su saldo pasó a «{ordered[lastCarry].Title}»"
                            : settlement.Status == SettlementStatus.Sent && settlement.ExpectedDate is null
                                ? "Sin fecha de cobro"
                                : Math.Abs(remaining) < 0.01m
                                    ? "Pendiente 0"
                                    : null;

                    if (reason is null) counted.Add((settlement, remaining));
                    else result.Add(new SettlementForecastState(settlement.Id, null, reason));
                }

                foreach (var (settlement, remaining) in counted)
                {
                    var description = counted.Count > 1
                        ? $"Liquidación con {name} ({settlement.Title})"
                        : $"Liquidación con {name}";
                    result.Add(new SettlementForecastState(
                        settlement.Id,
                        new SettlementExpectation(
                            settlement.Id,
                            settlement.CounterpartyId,
                            description,
                            remaining > 0m ? EntryDirection.In : EntryDirection.Out,
                            Math.Abs(remaining),
                            settlement.ExpectedDate ?? DefaultDate(settlement)),
                        null));
                }
            }

            return result;
        }

        public static async Task<List<SettlementExpectation>> LoadExpectationsAsync(
            IAppDbContext db,
            Guid userId,
            CancellationToken ct = default)
        {
            var settlements = await db.Settlements
                .AsNoTracking()
                .Include(settlement => settlement.Lines)
                .Where(settlement => settlement.UserId == userId)
                .ToListAsync(ct);
            if (settlements.Count == 0) return new List<SettlementExpectation>();

            var names = await db.Counterparties
                .AsNoTracking()
                .Where(person => person.UserId == userId)
                .ToDictionaryAsync(person => person.Id, person => person.Name, ct);

            return Evaluate(settlements, names)
                .Where(state => state.Expectation is not null)
                .Select(state => state.Expectation!)
                .ToList();
        }
    }
}
