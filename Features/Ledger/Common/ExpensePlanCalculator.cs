using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using Reminder = PersonalFinance.Api.Models.Entities.Reminder;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Shows, concept by concept, what comes in each month and its expenses split into fixed and variable.
    /// It is built on <see cref="MonthForecastCalculator"/>, so the month totals are the
    /// same ones the dashboard boxes, «Mes» and the AI show.
    /// </summary>
    public static class ExpensePlanCalculator
    {
        public const string ReminderGroupName = "Recordatorio";
        public const string SettlementGroupName = "Liquidaciones";

        /// <param name="concepts">All of the user's concepts, active or not (inactive ones only provide names).</param>
        /// <param name="rules">Active recurring rules.</param>
        /// <param name="reminders">Pending reminders with an amount; each one is a fixed row, or an income row when it is a cobro.</param>
        /// <param name="today">Defaults to the first day of <paramref name="currentMonth"/>.</param>
        /// <param name="settlements">What open settlements still expect: a fixed row what is to be paid, an income row what is to be collected.</param>
        public static ExpensePlanMonthsDto Build(
            IReadOnlyList<DateOnly> months,
            DateOnly currentMonth,
            IReadOnlyCollection<LedgerEntry> entries,
            IReadOnlyCollection<RecurringRule> rules,
            IReadOnlyCollection<Concept> concepts,
            IReadOnlyCollection<ConceptGroup> groups,
            IReadOnlyCollection<MonthlyBudget> budgets,
            IReadOnlySet<DateOnly> closedMonths,
            IReadOnlyCollection<Reminder>? reminders = null,
            DateOnly? today = null,
            IReadOnlyCollection<SettlementExpectation>? settlements = null)
        {
            var forecasts = MonthForecastCalculator.Build(new MonthForecastInput(
                months, today ?? currentMonth, entries, rules, concepts, budgets, reminders, settlements));
            var allById = concepts
                .GroupBy(concept => concept.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var groupById = groups.ToDictionary(group => group.Id);
            var variableExpenseConcepts = OrderConcepts(concepts.Where(concept =>
                concept.IsActive && concept.Kind == ConceptKind.Expense && concept.Nature == ConceptNature.Variable), groupById);
            var variableExpenseIds = variableExpenseConcepts.Select(concept => concept.Id).ToHashSet();

            var count = months.Count;
            var income = new RowTotals(count);
            var fixedExpense = new RowTotals(count);
            var variableCells = variableExpenseConcepts.ToDictionary(
                concept => concept.Id,
                _ => new ExpensePlanVariableCellDto[count]);
            var result = new ExpensePlanMonthsDto();

            for (var index = 0; index < count; index++)
            {
                var forecast = forecasts[index];
                foreach (var item in forecast.Items)
                {
                    var isIncome = item.Direction == EntryDirection.In;
                    var rows = isIncome ? income : fixedExpense;
                    if (item.Kind == ForecastItemKind.Reminder && item.ReminderId is { } reminderId)
                    {
                        rows.AddReminder(reminderId, item.Description, index, item.Amount);
                    }
                    else if (item.Kind == ForecastItemKind.Settlement && item.SettlementId is { } settlementId)
                    {
                        rows.AddSettlement(settlementId, item.Description, index, item.Amount);
                    }
                    // Income counts whole (also what a variable income is still expected to bring),
                    // so its rows add up to the month's income. Variable expenses have their own rows.
                    else if (item.ConceptId is { } conceptId &&
                             (isIncome ||
                              (item.Kind is ForecastItemKind.Entry or ForecastItemKind.RuleOccurrence &&
                               !variableExpenseIds.Contains(conceptId))))
                    {
                        rows.Add(conceptId, index, item.Amount, pending: !item.IsPaid);
                    }
                }

                // Variable concepts count max(planned, limit); a recurring payment inside one
                // counts inside its limit, never on top of it.
                var variableExpense = 0m;
                foreach (var concept in variableExpenseConcepts)
                {
                    var line = forecast.Variables.FirstOrDefault(variable => variable.ConceptId == concept.Id);
                    var counted = line?.Counted ?? 0m;
                    variableExpense += counted;
                    variableCells[concept.Id][index] = new ExpensePlanVariableCellDto
                    {
                        Budget = line?.Limit,
                        IsOverride = line?.IsOverride ?? false,
                        Spent = line?.Spent ?? 0m,
                        Planned = line?.Used ?? 0m,
                        Counted = counted
                    };
                }

                result.Months.Add(new ExpensePlanMonthDto
                {
                    Year = forecast.Year,
                    Month = forecast.Month,
                    IsCurrent = months[index] == currentMonth,
                    IsClosed = closedMonths.Contains(months[index]),
                    Income = forecast.Income,
                    FixedExpense = fixedExpense.Total(index),
                    VariableExpense = variableExpense
                });
            }

            result.Income = income.ToRows(allById, groupById);
            result.Fixed = fixedExpense.ToRows(allById, groupById);
            result.Variable = variableExpenseConcepts
                .Select(concept => new ExpensePlanVariableRowDto
                {
                    ConceptId = concept.Id,
                    Name = concept.Name,
                    GroupName = GroupName(groupById, concept.GroupId),
                    DefaultBudget = concept.DefaultMonthlyBudget,
                    Cells = variableCells[concept.Id].ToList()
                })
                .ToList();

            return result;
        }

        private static List<Concept> OrderConcepts(IEnumerable<Concept> concepts, IReadOnlyDictionary<Guid, ConceptGroup> groupById) =>
            concepts
                .OrderBy(concept => groupById.TryGetValue(concept.GroupId, out var group) ? group.SortOrder : int.MaxValue)
                .ThenBy(concept => concept.SortOrder)
                .ThenBy(concept => concept.Name)
                .ToList();

        private static string? GroupName(IReadOnlyDictionary<Guid, ConceptGroup> groupById, Guid groupId) =>
            groupById.TryGetValue(groupId, out var group) ? group.Name : null;

        /// <summary>
        /// Amounts per row and month for one side of the plan (what comes in, or the fixed expenses):
        /// one row per concept, then one per pending reminder and one per open settlement.
        /// </summary>
        private sealed class RowTotals
        {
            private readonly int _count;
            private readonly Dictionary<Guid, decimal[]> _amounts = new();
            private readonly Dictionary<Guid, bool[]> _pending = new();
            private readonly Dictionary<Guid, string> _reminderTitles = new();
            private readonly Dictionary<Guid, string> _settlementTitles = new();

            public RowTotals(int count) => _count = count;

            public void Add(Guid rowId, int index, decimal amount, bool pending)
            {
                if (!_amounts.TryGetValue(rowId, out var amounts))
                {
                    amounts = new decimal[_count];
                    _amounts[rowId] = amounts;
                    _pending[rowId] = new bool[_count];
                }

                amounts[index] += amount;
                if (pending) _pending[rowId][index] = true;
            }

            public void AddReminder(Guid reminderId, string title, int index, decimal amount)
            {
                _reminderTitles[reminderId] = title;
                Add(reminderId, index, amount, pending: true);
            }

            public void AddSettlement(Guid settlementId, string title, int index, decimal amount)
            {
                _settlementTitles[settlementId] = title;
                Add(settlementId, index, amount, pending: true);
            }

            public decimal Total(int index) => _amounts.Values.Sum(amounts => amounts[index]);

            public List<ExpensePlanFixedRowDto> ToRows(
                IReadOnlyDictionary<Guid, Concept> allById,
                IReadOnlyDictionary<Guid, ConceptGroup> groupById)
            {
                var rows = new List<(Guid Id, string Name, string? GroupName)>();
                rows.AddRange(OrderConcepts(_amounts.Keys.Where(allById.ContainsKey).Select(id => allById[id]), groupById)
                    .Select(concept => (concept.Id, concept.Name, GroupName(groupById, concept.GroupId))));
                // Defensive: an amount whose concept can't be found still has to show up somewhere.
                rows.AddRange(_amounts.Keys
                    .Where(id => !allById.ContainsKey(id) && !_reminderTitles.ContainsKey(id) && !_settlementTitles.ContainsKey(id))
                    .Select(id => (id, "Sin categoría", (string?)null)));
                rows.AddRange(Titled(_reminderTitles, allById, ReminderGroupName));
                rows.AddRange(Titled(_settlementTitles, allById, SettlementGroupName));

                return rows
                    .Where(row => _amounts[row.Id].Any(amount => amount != 0m))
                    .Select(row => new ExpensePlanFixedRowDto
                    {
                        ConceptId = row.Id,
                        Name = row.Name,
                        GroupName = row.GroupName,
                        Cells = _amounts[row.Id]
                            .Select((amount, index) => new ExpensePlanFixedCellDto
                            {
                                Amount = amount,
                                Paid = amount != 0m && !_pending[row.Id][index]
                            })
                            .ToList()
                    })
                    .ToList();
            }

            private static IEnumerable<(Guid Id, string Name, string? GroupName)> Titled(
                IReadOnlyDictionary<Guid, string> titles,
                IReadOnlyDictionary<Guid, Concept> allById,
                string groupName) =>
                titles
                    .Where(pair => !allById.ContainsKey(pair.Key))
                    .OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
                    .Select(pair => (pair.Key, pair.Value, (string?)groupName));
        }
    }
}
