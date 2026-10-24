using System.Globalization;
using PersonalFinance.Api.Features.Reminders;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public enum ForecastItemKind
    {
        /// <summary>A ledger entry already written in the month (paid or not).</summary>
        Entry,
        /// <summary>A recurring rule payment that has not been written as an entry yet.</summary>
        RuleOccurrence,
        /// <summary>What is still left of a variable concept's monthly limit.</summary>
        VariableReserve,
        /// <summary>A pending reminder with an amount: a pago (Out) or, when the reminder is marked as a cobro, In.</summary>
        Reminder,
        /// <summary>What an open settlement (mamá, Vane…) still expects: to collect or to pay.</summary>
        Settlement
    }

    public sealed record ForecastItem(
        ForecastItemKind Kind,
        string Id,
        DateOnly Date,
        EntryDirection Direction,
        decimal Amount,
        string Description,
        Guid? ConceptId,
        string? ConceptName,
        bool IsPaid = false,
        Guid? EntryId = null,
        Guid? RuleId = null,
        Guid? ReminderId = null,
        decimal? Limit = null,
        decimal? Used = null,
        bool IsOverdue = false,
        Guid? SettlementId = null);

    /// <summary>One variable concept in one month: its limit and how much of it is already taken.</summary>
    public sealed record ForecastVariableLine(
        Guid ConceptId,
        string ConceptName,
        EntryDirection Direction,
        decimal? Limit,
        bool IsOverride,
        decimal Used,
        decimal Spent,
        decimal Reserve)
    {
        /// <summary>What the month counts for this concept: max(used, limit).</summary>
        public decimal Counted => Used + Reserve;
    }

    public sealed class MonthForecast
    {
        public MonthForecast(int year, int month, IReadOnlyList<ForecastItem> items, IReadOnlyList<ForecastVariableLine> variables)
        {
            Year = year;
            Month = month;
            Items = items;
            Variables = variables;
            Income = Sum(items, item => item.Direction == EntryDirection.In);
            Expense = Sum(items, item => item.Direction == EntryDirection.Out);
            VariableExpenseReserve = Sum(items, item =>
                item.Kind == ForecastItemKind.VariableReserve && item.Direction == EntryDirection.Out);
            VariableIncomeExpected = Sum(items, item =>
                item.Kind == ForecastItemKind.VariableReserve && item.Direction == EntryDirection.In);
            Reminders = Sum(items, item =>
                item.Kind == ForecastItemKind.Reminder && item.Direction == EntryDirection.Out);
            ReminderIncome = Sum(items, item =>
                item.Kind == ForecastItemKind.Reminder && item.Direction == EntryDirection.In);
            RuleIncome = Sum(items, item =>
                item.Kind == ForecastItemKind.RuleOccurrence && item.Direction == EntryDirection.In);
            RuleExpense = Sum(items, item =>
                item.Kind == ForecastItemKind.RuleOccurrence && item.Direction == EntryDirection.Out);
            SettlementIncome = Sum(items, item =>
                item.Kind == ForecastItemKind.Settlement && item.Direction == EntryDirection.In);
            SettlementExpense = Sum(items, item =>
                item.Kind == ForecastItemKind.Settlement && item.Direction == EntryDirection.Out);
        }

        public int Year { get; }
        public int Month { get; }
        public DateOnly MonthStart => new(Year, Month, 1);
        public IReadOnlyList<ForecastItem> Items { get; }
        public IReadOnlyList<ForecastVariableLine> Variables { get; }
        public decimal Income { get; }
        public decimal Expense { get; }
        public decimal Balance => Income - Expense;
        public decimal VariableExpenseReserve { get; }
        public decimal VariableIncomeExpected { get; }
        /// <summary>Reminders that are pagos (money going out).</summary>
        public decimal Reminders { get; }
        /// <summary>Reminders that are cobros (money coming in).</summary>
        public decimal ReminderIncome { get; }
        public decimal RuleIncome { get; }
        public decimal RuleExpense { get; }
        public decimal SettlementIncome { get; }
        public decimal SettlementExpense { get; }

        private static decimal Sum(IEnumerable<ForecastItem> items, Func<ForecastItem, bool> predicate) =>
            items.Where(predicate).Sum(item => item.Amount);
    }

    /// <param name="Concepts">All of the user's concepts, active or not (inactive ones only provide names).</param>
    /// <param name="Rules">Recurring rules; inactive ones are ignored.</param>
    /// <param name="Reminders">Reminders; done ones and those without an amount are ignored.</param>
    /// <param name="Settlements">What open settlements still expect (see <see cref="SettlementForecast"/>).</param>
    public sealed record MonthForecastInput(
        IReadOnlyList<DateOnly> Months,
        DateOnly Today,
        IReadOnlyCollection<LedgerEntry> Entries,
        IReadOnlyCollection<RecurringRule> Rules,
        IReadOnlyCollection<Concept> Concepts,
        IReadOnlyCollection<MonthlyBudget> Budgets,
        IReadOnlyCollection<Reminder>? Reminders = null,
        IReadOnlyCollection<SettlementExpectation>? Settlements = null);

    /// <summary>
    /// The single source of truth for "how much comes in and goes out" in a month.
    /// Dashboard boxes, the expense plan, the «Mes» forecast and the AI all use it.
    /// A month is: its entries (not skipped, not transfers) + the recurring rule
    /// payments not written yet + what is left of each variable concept's limit
    /// + pending reminders with an amount + what open settlements still expect.
    /// A rule payment inside a variable concept counts inside that concept's limit,
    /// never on top of it.
    /// </summary>
    public static class MonthForecastCalculator
    {
        public static IReadOnlyList<MonthForecast> Build(MonthForecastInput input)
        {
            var conceptById = input.Concepts
                .GroupBy(concept => concept.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var variableConcepts = input.Concepts
                .Where(concept =>
                    concept.IsActive &&
                    concept.Nature == ConceptNature.Variable &&
                    concept.Kind is ConceptKind.Expense or ConceptKind.Income)
                .ToList();
            var budgetByKey = input.Budgets
                .GroupBy(budget => (budget.ConceptId, budget.Year, budget.Month))
                .ToDictionary(group => group.Key, group => group.First().LimitAmount);
            var calendarStart = new DateOnly(input.Today.Year, input.Today.Month, 1);

            var reminderOccurrences = new List<(Reminder Reminder, DateOnly Date, bool IsOverdue)>();
            if (input.Months.Count > 0 && input.Reminders is { Count: > 0 })
            {
                var lastMonth = input.Months.Max();
                var horizon = new DateOnly(lastMonth.Year, lastMonth.Month, 1).AddMonths(1).AddDays(-1);
                foreach (var reminder in input.Reminders)
                {
                    foreach (var occurrence in ReminderForecast.Occurrences(reminder, input.Today, horizon))
                        reminderOccurrences.Add((reminder, occurrence.Date, occurrence.IsOverdue));
                }
            }

            string ConceptName(Guid conceptId) =>
                conceptById.TryGetValue(conceptId, out var concept) ? concept.Name : "Sin categoría";

            string Describe(string? description, Guid conceptId) =>
                string.IsNullOrWhiteSpace(description) ? ConceptName(conceptId) : description.Trim();

            var result = new List<MonthForecast>(input.Months.Count);
            foreach (var monthDate in input.Months)
            {
                var year = monthDate.Year;
                var month = monthDate.Month;
                var monthStart = new DateOnly(year, month, 1);
                var monthEnd = monthStart.AddMonths(1).AddDays(-1);
                var items = new List<ForecastItem>();
                var materializedRuleIds = new HashSet<Guid>();

                foreach (var entry in input.Entries)
                {
                    if (entry.DueDate < monthStart || entry.DueDate > monthEnd) continue;
                    // Any entry of the rule in the month (even skipped) means the rule is already written.
                    if (entry.RecurringRuleId is { } entryRuleId) materializedRuleIds.Add(entryRuleId);
                    if (entry.Status == EntryStatus.Skipped || entry.IsTransfer) continue;

                    var isPaid = entry.Status == EntryStatus.Paid;
                    items.Add(new ForecastItem(
                        ForecastItemKind.Entry,
                        entry.Id.ToString(),
                        entry.DueDate,
                        entry.Direction,
                        isPaid ? entry.ActualAmount ?? entry.ForecastAmount : entry.ForecastAmount,
                        Describe(entry.Description, entry.ConceptId),
                        entry.ConceptId,
                        ConceptName(entry.ConceptId),
                        IsPaid: isPaid,
                        EntryId: entry.Id,
                        RuleId: entry.RecurringRuleId));
                }

                foreach (var rule in input.Rules)
                {
                    if (!rule.IsActive ||
                        rule.ForecastAmount <= 0m ||
                        materializedRuleIds.Contains(rule.Id) ||
                        !RecurrenceCalculator.OccursIn(rule, year, month))
                        continue;

                    items.Add(new ForecastItem(
                        ForecastItemKind.RuleOccurrence,
                        ForecastIds.Rule(rule.Id, year, month),
                        RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, year, month),
                        rule.Direction,
                        rule.ForecastAmount,
                        Describe(rule.Description, rule.ConceptId),
                        rule.ConceptId,
                        ConceptName(rule.ConceptId),
                        RuleId: rule.Id));
                }

                var reserveDate = monthStart <= input.Today && input.Today <= monthEnd ? input.Today : monthStart;
                var variables = new List<ForecastVariableLine>();
                foreach (var concept in variableConcepts)
                {
                    var direction = concept.Kind == ConceptKind.Expense ? EntryDirection.Out : EntryDirection.In;
                    var isOverride = budgetByKey.TryGetValue((concept.Id, year, month), out var monthLimit);
                    var limit = isOverride ? monthLimit : concept.DefaultMonthlyBudget;
                    var conceptItems = items
                        .Where(item =>
                            item.ConceptId == concept.Id &&
                            item.Direction == direction &&
                            item.Kind is ForecastItemKind.Entry or ForecastItemKind.RuleOccurrence)
                        .ToList();
                    var used = conceptItems.Sum(item => item.Amount);
                    var spent = conceptItems.Where(item => item.IsPaid).Sum(item => item.Amount);
                    var reserve = Math.Max(0m, (limit ?? 0m) - used);
                    variables.Add(new ForecastVariableLine(
                        concept.Id, concept.Name, direction, limit, isOverride, used, spent, reserve));

                    if (reserve > 0m)
                    {
                        items.Add(new ForecastItem(
                            ForecastItemKind.VariableReserve,
                            ForecastIds.Reserve(concept.Id, year, month),
                            reserveDate,
                            direction,
                            reserve,
                            concept.Name,
                            concept.Id,
                            concept.Name,
                            Limit: limit,
                            Used: used));
                    }
                }

                foreach (var occurrence in reminderOccurrences)
                {
                    if (occurrence.Date < monthStart || occurrence.Date > monthEnd) continue;
                    items.Add(new ForecastItem(
                        ForecastItemKind.Reminder,
                        ForecastIds.Reminder(occurrence.Reminder.Id, occurrence.Date),
                        occurrence.Date,
                        occurrence.Reminder.IsIncome ? EntryDirection.In : EntryDirection.Out,
                        occurrence.Reminder.Amount!.Value,
                        occurrence.Reminder.Title,
                        null,
                        "Recordatorio",
                        ReminderId: occurrence.Reminder.Id,
                        IsOverdue: occurrence.IsOverdue));
                }

                foreach (var expectation in input.Settlements ?? Array.Empty<SettlementExpectation>())
                {
                    if (expectation.Amount <= 0m) continue;
                    // Like reminders: a date before this month is still unpaid, so it counts on the 1st.
                    var date = expectation.Date < calendarStart ? calendarStart : expectation.Date;
                    if (date < monthStart || date > monthEnd) continue;
                    items.Add(new ForecastItem(
                        ForecastItemKind.Settlement,
                        ForecastIds.Settlement(expectation.SettlementId),
                        date,
                        expectation.Direction,
                        expectation.Amount,
                        expectation.Description,
                        null,
                        SettlementForecast.ConceptName,
                        IsOverdue: expectation.Date < input.Today,
                        SettlementId: expectation.SettlementId));
                }

                var ordered = items
                    .OrderBy(item => item.Date)
                    .ThenBy(item => item.Kind)
                    .ThenBy(item => item.Description, StringComparer.CurrentCultureIgnoreCase)
                    .ToList();
                result.Add(new MonthForecast(year, month, ordered, variables));
            }

            return result;
        }
    }

    /// <summary>
    /// Ids of forecast items that are not entries. Entries keep their plain GUID.
    /// rule:{ruleId}:{yyyy-MM} · reserve:{conceptId}:{yyyy-MM} · reminder:{reminderId}:{yyyy-MM-dd} · settlement:{settlementId}
    /// </summary>
    public static class ForecastIds
    {
        public const string RulePrefix = "rule:";
        public const string ReservePrefix = "reserve:";
        public const string ReminderPrefix = "reminder:";
        public const string SettlementPrefix = "settlement:";

        public static string Rule(Guid ruleId, int year, int month) => $"{RulePrefix}{ruleId}:{MonthKey(year, month)}";

        public static string Reserve(Guid conceptId, int year, int month) => $"{ReservePrefix}{conceptId}:{MonthKey(year, month)}";

        public static string Reminder(Guid reminderId, DateOnly date) =>
            $"{ReminderPrefix}{reminderId}:{date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)}";

        public static string Settlement(Guid settlementId) => $"{SettlementPrefix}{settlementId}";

        public static bool TryParseSettlement(string? id, out Guid settlementId)
        {
            settlementId = Guid.Empty;
            var parts = Split(id, SettlementPrefix);
            return parts is { Length: 1 } && Guid.TryParse(parts[0], out settlementId);
        }

        public static bool TryParseRule(string? id, out Guid ruleId, out int year, out int month) =>
            TryParseMonthId(id, RulePrefix, out ruleId, out year, out month);

        public static bool TryParseReserve(string? id, out Guid conceptId, out int year, out int month) =>
            TryParseMonthId(id, ReservePrefix, out conceptId, out year, out month);

        /// <summary>Accepts "reminder:{id}" and "reminder:{id}:{yyyy-MM-dd}".</summary>
        public static bool TryParseReminder(string? id, out Guid reminderId)
        {
            reminderId = Guid.Empty;
            var parts = Split(id, ReminderPrefix);
            return parts is { Length: 1 or 2 } && Guid.TryParse(parts[0], out reminderId);
        }

        private static string MonthKey(int year, int month) =>
            year.ToString("D4", CultureInfo.InvariantCulture) + "-" + month.ToString("D2", CultureInfo.InvariantCulture);

        private static bool TryParseMonthId(string? id, string prefix, out Guid targetId, out int year, out int month)
        {
            targetId = Guid.Empty;
            year = 0;
            month = 0;
            var parts = Split(id, prefix);
            if (parts is not { Length: 2 } || !Guid.TryParse(parts[0], out targetId)) return false;
            if (!DateTime.TryParseExact(parts[1], "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
            {
                targetId = Guid.Empty;
                return false;
            }

            year = parsed.Year;
            month = parsed.Month;
            return true;
        }

        private static string[]? Split(string? id, string prefix)
        {
            var trimmed = id?.Trim();
            if (string.IsNullOrEmpty(trimmed) || !trimmed.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return null;
            return trimmed[prefix.Length..].Split(':', StringSplitOptions.TrimEntries);
        }
    }

    public static class ReminderForecast
    {
        private const int MaxSteps = 1000;

        /// <summary>
        /// Occurrences of a pending reminder with an amount, from the current calendar month up to
        /// <paramref name="to"/>. A due date before this month is still unpaid, so it counts once on
        /// the 1st of this month (overdue) and the older repeats are not counted again.
        /// </summary>
        public static IEnumerable<(DateOnly Date, bool IsOverdue)> Occurrences(Reminder reminder, DateOnly today, DateOnly to)
        {
            if (reminder.IsDone || reminder.Amount is not > 0m) yield break;

            var calendarStart = new DateOnly(today.Year, today.Month, 1);
            if (to < calendarStart) yield break;

            var due = reminder.DueDate;
            if (due < calendarStart)
            {
                yield return (calendarStart, true);
                if (reminder.Repeat == ReminderRepeat.None) yield break;

                for (var steps = 0; due < calendarStart; steps++)
                {
                    var next = Next(reminder, due);
                    if (next <= due || steps >= MaxSteps) yield break;
                    due = next;
                }
            }

            for (var steps = 0; due <= to && steps < MaxSteps; steps++)
            {
                yield return (due, due < today);
                if (reminder.Repeat == ReminderRepeat.None) yield break;

                var next = Next(reminder, due);
                if (next <= due) yield break;
                due = next;
            }
        }

        private static DateOnly Next(Reminder reminder, DateOnly due) =>
            ReminderRules.NextDueDate(new Reminder
            {
                DueDate = due,
                AnchorDay = reminder.AnchorDay,
                Repeat = reminder.Repeat
            });
    }
}
