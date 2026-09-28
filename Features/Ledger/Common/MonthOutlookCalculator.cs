using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record MonthOutlookInput(
        int Year,
        int Month,
        DateOnly Today,
        IReadOnlyList<Account> Accounts,
        IReadOnlyList<LedgerEntry> PaidEntries,
        IReadOnlyList<LedgerEntry> Entries,
        IReadOnlyList<RecurringRule> Rules,
        IReadOnlyList<Concept> Concepts,
        IReadOnlyList<MonthlyBudget> Budgets,
        IReadOnlyDictionary<Guid, decimal> ReconciledBalances);

    /// <summary>
    /// Proyecta cada cuenta hasta fin del mes seleccionado a partir de su saldo
    /// real, los asientos sin liquidar y las reglas recurrentes que aún no se
    /// han materializado. Los meses anteriores al actual no se proyectan: sus
    /// importes sin pagar se consideran vencidos.
    /// </summary>
    public static class MonthOutlookCalculator
    {
        private const decimal Tolerance = 0.01m;

        private sealed record PendingItem(
            DateOnly DueDate,
            string Description,
            Guid ConceptId,
            string ConceptName,
            Guid? AccountId,
            EntryDirection Direction,
            decimal Amount,
            bool IsTransfer,
            bool FromRule = false,
            bool IsVariableReserve = false)
        {
            public decimal Signed => Direction == EntryDirection.In ? Amount : -Amount;
        }

        public static MonthOutlookDto Build(MonthOutlookInput input, bool includeVariableReserve = true)
        {
            var monthStart = new DateOnly(input.Year, input.Month, 1);
            var monthEnd = monthStart.AddMonths(1).AddDays(-1);
            var calendarStart = new DateOnly(input.Today.Year, input.Today.Month, 1);
            var isPast = monthEnd < input.Today;
            var isCurrent = monthStart <= input.Today && input.Today <= monthEnd;
            var conceptById = input.Concepts.ToDictionary(concept => concept.Id);
            var accountById = input.Accounts.ToDictionary(account => account.Id);

            // Lo anterior al inicio contable de las cuentas (histórico descartado)
            // no se tiene en cuenta para lo que no tiene cuenta asignada.
            var unassignedFloor = input.Accounts.Count == 0
                ? calendarStart
                : Max(calendarStart, input.Accounts.Min(account => account.OpeningDate));

            var windowStart = isPast ? monthStart : Min(calendarStart, monthStart);
            var pending = CollectPendingItems(input, conceptById, windowStart, monthEnd)
                .Where(item =>
                {
                    var floor = item.AccountId is Guid accountId && accountById.TryGetValue(accountId, out var account)
                        ? Max(isPast ? monthStart : calendarStart, account.OpeningDate)
                        : isPast ? Max(monthStart, unassignedFloor) : unassignedFloor;
                    return item.DueDate >= floor;
                })
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.Direction == EntryDirection.In ? 0 : 1)
                .ToList();

            var variableReserves = isPast || !includeVariableReserve
                ? new List<(PendingItem Item, decimal Amount)>()
                : CollectVariableReserveItems(input, windowStart, monthEnd, pending);
            pending.AddRange(variableReserves.Select(reserve => reserve.Item));
            var selectedMonthReserve = variableReserves
                .Where(reserve => reserve.Item.DueDate >= monthStart && reserve.Item.DueDate <= monthEnd)
                .Sum(reserve => reserve.Amount);
            pending = pending
                .OrderBy(item => item.DueDate)
                .ThenBy(item => item.Direction == EntryDirection.In ? 0 : 1)
                .ToList();

            var carryItems = pending.Where(item => item.DueDate < monthStart).ToList();
            var monthItems = pending.Where(item => item.DueDate >= monthStart).ToList();

            var outlook = new MonthOutlookDto
            {
                Year = input.Year,
                Month = input.Month,
                IsPast = isPast,
                IsCurrent = isCurrent
            };

            foreach (var account in input.Accounts)
            {
                var actualDate = isPast ? monthEnd : Max(input.Today, account.OpeningDate);
                var actualBalance = account.OpeningDate > actualDate
                    ? 0m
                    : AccountBalanceCalculator.ComputeBalance(account, input.PaidEntries, actualDate);
                var accountMonthItems = monthItems.Where(item => item.AccountId == account.Id).ToList();
                var baseBalance = isPast
                    ? actualBalance
                    : actualBalance + carryItems.Where(item => item.AccountId == account.Id).Sum(item => item.Signed);

                var pendingIncome = accountMonthItems
                    .Where(item => item.Direction == EntryDirection.In)
                    .Sum(item => item.Amount);
                var pendingExpense = accountMonthItems
                    .Where(item => item.Direction == EntryDirection.Out)
                    .Sum(item => item.Amount);

                var running = baseBalance;
                var lowest = baseBalance;
                DateOnly? lowestDate = null;
                if (!isPast)
                {
                    foreach (var item in accountMonthItems)
                    {
                        running += item.Signed;
                        if (running < lowest)
                        {
                            lowest = running;
                            lowestDate = item.DueDate;
                        }
                    }
                }

                input.ReconciledBalances.TryGetValue(account.Id, out var reconciled);
                var hasReconciliation = input.ReconciledBalances.ContainsKey(account.Id);

                outlook.Accounts.Add(new AccountOutlookDto
                {
                    AccountId = account.Id,
                    Name = account.Name,
                    BaseBalance = baseBalance,
                    PendingIncome = pendingIncome,
                    PendingExpense = pendingExpense,
                    ProjectedEndBalance = isPast ? actualBalance : baseBalance + pendingIncome - pendingExpense,
                    LowestBalance = lowest,
                    LowestBalanceDate = lowestDate,
                    Shortfall = lowest < 0m ? -lowest : 0m,
                    ActualBalance = actualBalance,
                    ReconciledBalance = hasReconciliation ? reconciled : null,
                    IsReconciled = hasReconciliation && Math.Abs(reconciled - actualBalance) <= Tolerance
                });
            }

            var unassignedMonth = monthItems.Where(item => item.AccountId is null).ToList();
            var unassignedCarry = carryItems.Where(item => item.AccountId is null).Sum(item => item.Signed);
            outlook.Unassigned = new UnassignedOutlookDto
            {
                PendingIncome = unassignedMonth.Where(item => item.Direction == EntryDirection.In).Sum(item => item.Amount),
                PendingExpense = unassignedMonth.Where(item => item.Direction == EntryDirection.Out).Sum(item => item.Amount)
            };

            if (!isPast)
            {
                var (expenseReserve, incomeExpected) = VariableRemainders(input, monthStart, monthEnd, pending);
                outlook.Unassigned.VariableExpenseReserve = includeVariableReserve ? expenseReserve : 0m;
                outlook.Unassigned.VariableIncomeExpected = incomeExpected;
                outlook.VariableExpenseReserve = selectedMonthReserve
                    + outlook.Unassigned.VariableExpenseReserve;
            }

            outlook.PendingItems = monthItems.Select(item => ToDto(item, accountById, input.Today)).ToList();
            outlook.OverdueItems = pending
                .Where(item => item.DueDate < input.Today)
                .Select(item => ToDto(item, accountById, input.Today))
                .ToList();

            if (!isPast) SuggestTransfers(outlook);

            outlook.FreeMoney = outlook.Accounts.Sum(account => account.ProjectedEndBalance)
                + (isPast ? 0m : unassignedCarry)
                + outlook.Unassigned.PendingIncome
                - outlook.Unassigned.PendingExpense
                - outlook.Unassigned.VariableExpenseReserve
                + outlook.Unassigned.VariableIncomeExpected;

            outlook.Deviations = BuildDeviations(input, conceptById, monthStart, monthEnd, monthItems);

            return outlook;
        }

        private static List<(PendingItem Item, decimal Amount)> CollectVariableReserveItems(
            MonthOutlookInput input,
            DateOnly windowStart,
            DateOnly monthEnd,
            IReadOnlyList<PendingItem> pending)
        {
            var reserves = new List<(PendingItem Item, decimal Amount)>();
            var calendarStart = new DateOnly(input.Today.Year, input.Today.Month, 1);
            var start = Max(windowStart, calendarStart);

            for (var cursor = start; cursor <= monthEnd; cursor = cursor.AddMonths(1))
            {
                var monthStart = new DateOnly(cursor.Year, cursor.Month, 1);
                var currentMonthEnd = monthStart.AddMonths(1).AddDays(-1);
                var dueDate = monthStart == calendarStart ? input.Today : monthStart;

                foreach (var concept in input.Concepts.Where(concept =>
                             concept.Kind == ConceptKind.Expense &&
                             concept.Nature == ConceptNature.Variable &&
                             concept.AccountId.HasValue))
                {
                    var limit = input.Budgets.FirstOrDefault(budget =>
                            budget.ConceptId == concept.Id &&
                            budget.Year == monthStart.Year &&
                            budget.Month == monthStart.Month)?.LimitAmount
                        ?? concept.DefaultMonthlyBudget
                        ?? 0m;
                    if (limit <= 0m) continue;

                    var used = input.Entries
                        .Where(entry =>
                            entry.ConceptId == concept.Id &&
                            entry.Status == EntryStatus.Paid &&
                            !entry.IsTransfer &&
                            entry.DueDate >= monthStart &&
                            entry.DueDate <= currentMonthEnd)
                        .Sum(entry => entry.EffectiveAmount)
                        + pending
                            .Where(item =>
                                item.ConceptId == concept.Id &&
                                !item.IsTransfer &&
                                !item.IsVariableReserve &&
                                item.DueDate >= monthStart &&
                                item.DueDate <= currentMonthEnd)
                            .Sum(item => item.Amount);
                    var remainder = Math.Max(0m, limit - used);
                    if (remainder <= 0m) continue;

                    var accountId = concept.AccountId!.Value;
                    var account = input.Accounts.FirstOrDefault(item => item.Id == accountId);
                    if (account is null) continue;
                    var accountDueDate = Max(dueDate, account.OpeningDate);
                    if (accountDueDate > currentMonthEnd) continue;

                    reserves.Add((new PendingItem(
                        accountDueDate,
                        $"Reserva estimada: {concept.Name}",
                        concept.Id,
                        concept.Name,
                        accountId,
                        EntryDirection.Out,
                        remainder,
                        false,
                        IsVariableReserve: true), remainder));
                }
            }

            return reserves;
        }

        private static List<PendingItem> CollectPendingItems(
            MonthOutlookInput input,
            IReadOnlyDictionary<Guid, Concept> conceptById,
            DateOnly from,
            DateOnly to)
        {
            var items = input.Entries
                .Where(entry =>
                    entry.Status != EntryStatus.Paid &&
                    entry.Status != EntryStatus.Skipped &&
                    entry.DueDate >= from &&
                    entry.DueDate <= to)
                .Select(entry => new PendingItem(
                    entry.DueDate,
                    entry.Description ?? ConceptName(conceptById, entry.ConceptId),
                    entry.ConceptId,
                    ConceptName(conceptById, entry.ConceptId),
                    entry.AccountId,
                    entry.Direction,
                    entry.Remaining > 0m ? entry.Remaining : entry.ForecastAmount,
                    entry.IsTransfer))
                .ToList();

            // Las reglas que todavía no se han convertido en asientos también cuentan.
            for (var cursor = new DateOnly(from.Year, from.Month, 1); cursor <= to; cursor = cursor.AddMonths(1))
            {
                var year = cursor.Year;
                var month = cursor.Month;
                var materialized = input.Entries
                    .Where(entry =>
                        entry.RecurringRuleId != null &&
                        entry.DueDate.Year == year &&
                        entry.DueDate.Month == month)
                    .Select(entry => entry.RecurringRuleId!.Value)
                    .ToHashSet();

                foreach (var rule in input.Rules)
                {
                    if (!rule.IsActive || materialized.Contains(rule.Id)) continue;
                    if (!RecurrenceCalculator.OccursIn(rule, year, month)) continue;

                    var dueDate = RecurrenceCalculator.ResolveDueDate(rule.DayOfMonth, year, month);
                    if (dueDate < from || dueDate > to) continue;

                    var conceptName = ConceptName(conceptById, rule.ConceptId);
                    items.Add(new PendingItem(
                        dueDate,
                        rule.Description ?? conceptName,
                        rule.ConceptId,
                        conceptName,
                        rule.AccountId,
                        rule.Direction,
                        rule.ForecastAmount,
                        false,
                        FromRule: true));
                }
            }

            return items.Where(item => item.Amount > 0m).ToList();
        }

        private static (decimal ExpenseReserve, decimal IncomeExpected) VariableRemainders(
            MonthOutlookInput input,
            DateOnly monthStart,
            DateOnly monthEnd,
            IReadOnlyList<PendingItem> pending)
        {
            decimal expenseReserve = 0m;
            decimal incomeExpected = 0m;

            foreach (var concept in input.Concepts.Where(concept =>
                         concept.Nature == ConceptNature.Variable &&
                         (concept.Kind == ConceptKind.Income || concept.AccountId is null)))
            {
                var limit = input.Budgets.FirstOrDefault(budget =>
                        budget.ConceptId == concept.Id &&
                        budget.Year == monthStart.Year &&
                        budget.Month == monthStart.Month)?.LimitAmount
                    ?? concept.DefaultMonthlyBudget
                    ?? 0m;
                if (limit <= 0m) continue;

                var used = input.Entries
                    .Where(entry =>
                        entry.ConceptId == concept.Id &&
                        entry.Status == EntryStatus.Paid &&
                        !entry.IsTransfer &&
                        entry.DueDate >= monthStart &&
                        entry.DueDate <= monthEnd)
                    .Sum(entry => entry.EffectiveAmount)
                    + pending
                        .Where(item => item.ConceptId == concept.Id && item.DueDate >= monthStart)
                        .Sum(item => item.Amount);

                var remainder = Math.Max(0m, limit - used);
                if (concept.Kind == ConceptKind.Income) incomeExpected += remainder;
                else if (concept.Kind == ConceptKind.Expense) expenseReserve += remainder;
            }

            return (expenseReserve, incomeExpected);
        }

        /// <summary>
        /// Cubre el descubierto de cada cuenta con el margen de las demás. El
        /// margen de una cuenta es su saldo mínimo del mes: mover más dejaría
        /// en negativo a la cuenta de origen.
        /// </summary>
        private static void SuggestTransfers(MonthOutlookDto outlook)
        {
            var margins = outlook.Accounts
                .Where(account => account.LowestBalance > Tolerance)
                .ToDictionary(account => account.AccountId, account => account.LowestBalance);

            foreach (var target in outlook.Accounts
                         .Where(account => account.Shortfall > Tolerance)
                         .OrderBy(account => account.LowestBalanceDate))
            {
                var needed = target.Shortfall;
                foreach (var source in outlook.Accounts
                             .Where(account => account.AccountId != target.AccountId && margins.ContainsKey(account.AccountId))
                             .OrderByDescending(account => margins[account.AccountId]))
                {
                    if (needed <= Tolerance) break;

                    var amount = Math.Min(needed, margins[source.AccountId]);
                    if (amount <= Tolerance) continue;

                    margins[source.AccountId] -= amount;
                    needed -= amount;
                    outlook.SuggestedTransfers.Add(new TransferSuggestionDto
                    {
                        FromAccountId = source.AccountId,
                        FromAccountName = source.Name,
                        ToAccountId = target.AccountId,
                        ToAccountName = target.Name,
                        Amount = Math.Round(amount, 2),
                        Before = target.LowestBalanceDate
                    });
                }

                if (needed > Tolerance) outlook.UncoveredShortfall += Math.Round(needed, 2);
            }
        }

        private static List<ConceptDeviationDto> BuildDeviations(
            MonthOutlookInput input,
            IReadOnlyDictionary<Guid, Concept> conceptById,
            DateOnly monthStart,
            DateOnly monthEnd,
            IReadOnlyList<PendingItem> monthItems)
        {
            var monthEntries = input.Entries
                .Where(entry =>
                    !entry.IsTransfer &&
                    entry.Status != EntryStatus.Skipped &&
                    entry.DueDate >= monthStart &&
                    entry.DueDate <= monthEnd)
                .ToList();
            var ruleItems = monthItems.Where(item => item.FromRule).ToList();

            var deviations = new List<ConceptDeviationDto>();
            foreach (var concept in input.Concepts.Where(concept =>
                         concept.Kind == ConceptKind.Income || concept.Kind == ConceptKind.Expense))
            {
                var entries = monthEntries.Where(entry => entry.ConceptId == concept.Id).ToList();
                var actual = entries.Where(entry => entry.Status == EntryStatus.Paid).Sum(entry => entry.EffectiveAmount);
                var pending = monthItems
                    .Where(item => item.ConceptId == concept.Id && !item.IsTransfer && !item.IsVariableReserve)
                    .Sum(item => item.Amount);

                decimal planned;
                if (concept.Nature == ConceptNature.Variable)
                {
                    planned = input.Budgets.FirstOrDefault(budget =>
                            budget.ConceptId == concept.Id &&
                            budget.Year == monthStart.Year &&
                            budget.Month == monthStart.Month)?.LimitAmount
                        ?? concept.DefaultMonthlyBudget
                        ?? 0m;
                }
                else
                {
                    planned = entries.Sum(entry => entry.ForecastAmount)
                        + ruleItems.Where(item => item.ConceptId == concept.Id).Sum(item => item.Amount);
                }

                if (planned == 0m && actual == 0m && pending == 0m) continue;

                deviations.Add(new ConceptDeviationDto
                {
                    ConceptId = concept.Id,
                    Name = concept.Name,
                    Kind = concept.Kind,
                    Nature = concept.Nature,
                    Planned = planned,
                    Actual = actual,
                    Pending = pending,
                    Deviation = actual + pending - planned
                });
            }

            return deviations
                .OrderByDescending(deviation => Math.Abs(deviation.Deviation))
                .ThenBy(deviation => deviation.Name)
                .ToList();
        }

        private static OutlookItemDto ToDto(
            PendingItem item,
            IReadOnlyDictionary<Guid, Account> accountById,
            DateOnly today) => new()
        {
            DueDate = item.DueDate,
            Description = item.Description,
            ConceptName = item.ConceptName,
            AccountId = item.AccountId,
            AccountName = item.AccountId is Guid id && accountById.TryGetValue(id, out var account)
                ? account.Name
                : null,
            Direction = item.Direction,
            Amount = item.Amount,
            IsOverdue = item.DueDate < today,
            IsTransfer = item.IsTransfer,
            IsVariableReserve = item.IsVariableReserve
        };

        private static string ConceptName(IReadOnlyDictionary<Guid, Concept> conceptById, Guid conceptId) =>
            conceptById.TryGetValue(conceptId, out var concept) ? concept.Name : "Sin concepto";

        private static DateOnly Max(DateOnly left, DateOnly right) => left > right ? left : right;
        private static DateOnly Min(DateOnly left, DateOnly right) => left < right ? left : right;
    }
}
