using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    /// <summary>
    /// Splits each month's planned expenses into fixed and variable, concept by concept.
    /// The month totals follow the same rules as DashboardService.GetFutureProjectionAsync,
    /// so this screen and the dashboard show the same numbers.
    /// </summary>
    public static class ExpensePlanCalculator
    {
        /// <param name="concepts">All of the user's concepts, active or not (inactive ones only provide names).</param>
        /// <param name="rules">Active recurring rules.</param>
        public static ExpensePlanMonthsDto Build(
            IReadOnlyList<DateOnly> months,
            DateOnly currentMonth,
            IReadOnlyCollection<LedgerEntry> entries,
            IReadOnlyCollection<RecurringRule> rules,
            IReadOnlyCollection<Concept> concepts,
            IReadOnlyCollection<ConceptGroup> groups,
            IReadOnlyCollection<MonthlyBudget> budgets,
            IReadOnlySet<DateOnly> closedMonths)
        {
            var activeById = concepts.Where(concept => concept.IsActive).ToDictionary(concept => concept.Id);
            var allById = concepts.ToDictionary(concept => concept.Id);
            var groupById = groups.ToDictionary(group => group.Id);
            var variableExpenseConcepts = OrderConcepts(concepts.Where(concept =>
                concept.IsActive && concept.Kind == ConceptKind.Expense && concept.Nature == ConceptNature.Variable), groupById);
            var variableIncomeConcepts = concepts
                .Where(concept => concept.IsActive && concept.Kind == ConceptKind.Income && concept.Nature == ConceptNature.Variable)
                .ToList();

            var count = months.Count;
            var fixedAmounts = new Dictionary<Guid, decimal[]>();
            var fixedPending = new Dictionary<Guid, bool[]>();
            var variableCells = variableExpenseConcepts.ToDictionary(
                concept => concept.Id,
                _ => new ExpensePlanVariableCellDto[count]);
            var result = new ExpensePlanMonthsDto();

            bool IsFixed(LedgerEntry entry) =>
                !activeById.TryGetValue(entry.ConceptId, out var concept) || concept.Nature == ConceptNature.Fixed;

            decimal? Limit(Guid conceptId, int year, int month, out bool isOverride)
            {
                var monthLimit = budgets.FirstOrDefault(budget =>
                    budget.ConceptId == conceptId && budget.Year == year && budget.Month == month)?.LimitAmount;
                isOverride = monthLimit is not null;
                return monthLimit ?? (activeById.TryGetValue(conceptId, out var concept) ? concept.DefaultMonthlyBudget : null);
            }

            void AddFixed(Guid conceptId, int index, decimal amount, bool pending)
            {
                if (!fixedAmounts.TryGetValue(conceptId, out var amounts))
                {
                    amounts = new decimal[count];
                    fixedAmounts[conceptId] = amounts;
                    fixedPending[conceptId] = new bool[count];
                }

                amounts[index] += amount;
                if (pending) fixedPending[conceptId][index] = true;
            }

            for (var index = 0; index < count; index++)
            {
                var year = months[index].Year;
                var month = months[index].Month;
                var monthEntries = entries
                    .Where(entry =>
                        entry.Status != EntryStatus.Skipped &&
                        !entry.IsTransfer &&
                        entry.DueDate.Year == year &&
                        entry.DueDate.Month == month)
                    .ToList();
                var materializedRuleIds = entries
                    .Where(entry =>
                        entry.DueDate.Year == year &&
                        entry.DueDate.Month == month &&
                        entry.RecurringRuleId != null)
                    .Select(entry => entry.RecurringRuleId!.Value)
                    .ToHashSet();
                var missingRules = rules
                    .Where(rule => !materializedRuleIds.Contains(rule.Id) && RecurrenceCalculator.OccursIn(rule, year, month))
                    .ToList();

                var income = monthEntries
                        .Where(entry => entry.Direction == EntryDirection.In && IsFixed(entry))
                        .Sum(Amount)
                    + missingRules.Where(rule => rule.Direction == EntryDirection.In).Sum(rule => rule.ForecastAmount)
                    + variableIncomeConcepts.Sum(concept => Math.Max(
                        monthEntries
                            .Where(entry => entry.Direction == EntryDirection.In && entry.ConceptId == concept.Id)
                            .Sum(Amount),
                        Limit(concept.Id, year, month, out _) ?? 0m));

                foreach (var entry in monthEntries.Where(entry => entry.Direction == EntryDirection.Out && IsFixed(entry)))
                    AddFixed(entry.ConceptId, index, Amount(entry), entry.Status != EntryStatus.Paid);
                foreach (var rule in missingRules.Where(rule => rule.Direction == EntryDirection.Out))
                    AddFixed(rule.ConceptId, index, rule.ForecastAmount, pending: true);

                var variableExpense = 0m;
                foreach (var concept in variableExpenseConcepts)
                {
                    var conceptEntries = monthEntries
                        .Where(entry => entry.Direction == EntryDirection.Out && entry.ConceptId == concept.Id)
                        .ToList();
                    var planned = conceptEntries.Sum(Amount);
                    var budget = Limit(concept.Id, year, month, out var isOverride);
                    var counted = Math.Max(planned, budget ?? 0m);
                    variableExpense += counted;
                    variableCells[concept.Id][index] = new ExpensePlanVariableCellDto
                    {
                        Budget = budget,
                        IsOverride = isOverride,
                        Spent = conceptEntries.Where(entry => entry.Status == EntryStatus.Paid).Sum(Amount),
                        Planned = planned,
                        Counted = counted
                    };
                }

                result.Months.Add(new ExpensePlanMonthDto
                {
                    Year = year,
                    Month = month,
                    IsCurrent = months[index] == currentMonth,
                    IsClosed = closedMonths.Contains(months[index]),
                    Income = income,
                    FixedExpense = fixedAmounts.Values.Sum(amounts => amounts[index]),
                    VariableExpense = variableExpense
                });
            }

            var fixedRows = new List<(Guid Id, string Name, Guid GroupId)>();
            fixedRows.AddRange(OrderConcepts(
                    fixedAmounts.Keys.Where(allById.ContainsKey).Select(id => allById[id]),
                    groupById)
                .Select(concept => (concept.Id, concept.Name, concept.GroupId)));
            // Defensive: an amount whose concept can't be found still has to show up somewhere.
            fixedRows.AddRange(fixedAmounts.Keys
                .Where(id => !allById.ContainsKey(id))
                .Select(id => (id, "Sin categoría", Guid.Empty)));

            result.Fixed = fixedRows
                .Where(row => fixedAmounts[row.Id].Any(amount => amount != 0m))
                .Select(row => new ExpensePlanFixedRowDto
                {
                    ConceptId = row.Id,
                    Name = row.Name,
                    GroupName = groupById.TryGetValue(row.GroupId, out var group) ? group.Name : null,
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
                    GroupName = groupById.TryGetValue(concept.GroupId, out var group) ? group.Name : null,
                    DefaultBudget = concept.DefaultMonthlyBudget,
                    Cells = variableCells[concept.Id].ToList()
                })
                .ToList();

            return result;
        }

        private static decimal Amount(LedgerEntry entry) =>
            entry.Status == EntryStatus.Paid ? entry.ActualAmount ?? entry.ForecastAmount : entry.ForecastAmount;

        private static List<Concept> OrderConcepts(IEnumerable<Concept> concepts, IReadOnlyDictionary<Guid, ConceptGroup> groupById) =>
            concepts
                .OrderBy(concept => groupById.TryGetValue(concept.GroupId, out var group) ? group.SortOrder : int.MaxValue)
                .ThenBy(concept => concept.SortOrder)
                .ThenBy(concept => concept.Name)
                .ToList();
    }
}
