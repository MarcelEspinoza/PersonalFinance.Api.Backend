using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using Reminder = PersonalFinance.Api.Models.Entities.Reminder;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Splits each month's expenses into fixed and variable, concept by concept.
    /// It is built on <see cref="MonthForecastCalculator"/>, so the month totals are the
    /// same ones the dashboard boxes, «Mes» and the AI show.
    /// </summary>
    public static class ExpensePlanCalculator
    {
        public const string ReminderGroupName = "Recordatorio";

        /// <param name="concepts">All of the user's concepts, active or not (inactive ones only provide names).</param>
        /// <param name="rules">Active recurring rules.</param>
        /// <param name="reminders">Pending reminders with an amount; each one is shown as a fixed row.</param>
        /// <param name="today">Defaults to the first day of <paramref name="currentMonth"/>.</param>
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
            DateOnly? today = null)
        {
            var forecasts = MonthForecastCalculator.Build(new MonthForecastInput(
                months, today ?? currentMonth, entries, rules, concepts, budgets, reminders));
            var allById = concepts
                .GroupBy(concept => concept.Id)
                .ToDictionary(group => group.Key, group => group.First());
            var groupById = groups.ToDictionary(group => group.Id);
            var variableExpenseConcepts = OrderConcepts(concepts.Where(concept =>
                concept.IsActive && concept.Kind == ConceptKind.Expense && concept.Nature == ConceptNature.Variable), groupById);
            var variableExpenseIds = variableExpenseConcepts.Select(concept => concept.Id).ToHashSet();

            var count = months.Count;
            var fixedAmounts = new Dictionary<Guid, decimal[]>();
            var fixedPending = new Dictionary<Guid, bool[]>();
            var reminderTitles = new Dictionary<Guid, string>();
            var variableCells = variableExpenseConcepts.ToDictionary(
                concept => concept.Id,
                _ => new ExpensePlanVariableCellDto[count]);
            var result = new ExpensePlanMonthsDto();

            string? GroupName(Guid groupId) => groupById.TryGetValue(groupId, out var group) ? group.Name : null;

            void AddFixed(Guid rowId, int index, decimal amount, bool pending)
            {
                if (!fixedAmounts.TryGetValue(rowId, out var amounts))
                {
                    amounts = new decimal[count];
                    fixedAmounts[rowId] = amounts;
                    fixedPending[rowId] = new bool[count];
                }

                amounts[index] += amount;
                if (pending) fixedPending[rowId][index] = true;
            }

            for (var index = 0; index < count; index++)
            {
                var forecast = forecasts[index];
                foreach (var item in forecast.Items.Where(item => item.Direction == EntryDirection.Out))
                {
                    if (item.Kind == ForecastItemKind.Reminder && item.ReminderId is { } reminderId)
                    {
                        reminderTitles[reminderId] = item.Description;
                        AddFixed(reminderId, index, item.Amount, pending: true);
                    }
                    else if ((item.Kind is ForecastItemKind.Entry or ForecastItemKind.RuleOccurrence) &&
                             item.ConceptId is { } conceptId &&
                             !variableExpenseIds.Contains(conceptId))
                    {
                        AddFixed(conceptId, index, item.Amount, pending: !item.IsPaid);
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
                    FixedExpense = fixedAmounts.Values.Sum(amounts => amounts[index]),
                    VariableExpense = variableExpense
                });
            }

            var fixedRows = new List<(Guid Id, string Name, string? GroupName)>();
            fixedRows.AddRange(OrderConcepts(
                    fixedAmounts.Keys.Where(allById.ContainsKey).Select(id => allById[id]),
                    groupById)
                .Select(concept => (concept.Id, concept.Name, GroupName(concept.GroupId))));
            // Defensive: an amount whose concept can't be found still has to show up somewhere.
            fixedRows.AddRange(fixedAmounts.Keys
                .Where(id => !allById.ContainsKey(id) && !reminderTitles.ContainsKey(id))
                .Select(id => (id, "Sin categoría", (string?)null)));
            fixedRows.AddRange(reminderTitles
                .Where(pair => !allById.ContainsKey(pair.Key))
                .OrderBy(pair => pair.Value, StringComparer.CurrentCultureIgnoreCase)
                .Select(pair => (pair.Key, pair.Value, (string?)ReminderGroupName)));

            result.Fixed = fixedRows
                .Where(row => fixedAmounts[row.Id].Any(amount => amount != 0m))
                .Select(row => new ExpensePlanFixedRowDto
                {
                    ConceptId = row.Id,
                    Name = row.Name,
                    GroupName = row.GroupName,
                    Cells = fixedAmounts[row.Id]
                        .Select((amount, index) => new ExpensePlanFixedCellDto
                        {
                            Amount = amount,
                            Paid = amount != 0m && !fixedPending[row.Id][index]
                        })
                        .ToList()
                })
                .ToList();

            result.Variable = variableExpenseConcepts
                .Select(concept => new ExpensePlanVariableRowDto
                {
                    ConceptId = concept.Id,
                    Name = concept.Name,
                    GroupName = GroupName(concept.GroupId),
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
    }
}
