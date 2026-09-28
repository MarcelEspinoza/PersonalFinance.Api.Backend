using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Enums;
using System.Globalization;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class DashboardService : IDashboardService
    {
        private readonly AppDbContext _db;
        private readonly ISavingService _savingService;
        private readonly ICommitmentService _commitmentService;
        private readonly ICommitmentMatchingService _commitmentMatchingService;
        private readonly IBudgetService _budgetService;
        private readonly IHttpContextAccessor _http;

        public DashboardService(
            AppDbContext db,
            ISavingService savingService,
            ICommitmentService commitmentService,
            ICommitmentMatchingService commitmentMatchingService,
            IBudgetService budgetService,
            IHttpContextAccessor http)
        {
            _db = db;
            _savingService = savingService;
            _commitmentService = commitmentService;
            _commitmentMatchingService = commitmentMatchingService;
            _budgetService = budgetService;
            _http = http;
        }

        private Guid CurrentUserId()
        {
            var id = _http.HttpContext?.User?
                .FindFirst(ClaimTypes.NameIdentifier)?.Value;

            if (!Guid.TryParse(id, out var userId))
                throw new UnauthorizedAccessException();

            return userId;
        }

        public async Task<DashboardProjectionResult> GetFutureProjectionAsync(
            int? requestedYear = null,
            int? requestedMonth = null,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(account => account.UserId == userId && account.IsActive)
                .OrderBy(account => account.Name)
                .ToListAsync(ct);
            var calendarMonth = new DateOnly(today.Year, today.Month, 1);
            var latestOpeningMonth = accounts.Count == 0
                ? calendarMonth
                : new DateOnly(
                    accounts.Max(account => account.OpeningDate).Year,
                    accounts.Max(account => account.OpeningDate).Month,
                    1);
            var defaultMonth = latestOpeningMonth > calendarMonth ? latestOpeningMonth : calendarMonth;
            var currentMonth = requestedYear is >= 2000 and <= 2100 && requestedMonth is >= 1 and <= 12
                ? new DateOnly(requestedYear.Value, requestedMonth.Value, 1)
                : defaultMonth;
            var minMonth = accounts.Count == 0
                ? defaultMonth
                : new DateOnly(
                    accounts.Min(account => account.OpeningDate).Year,
                    accounts.Min(account => account.OpeningDate).Month,
                    1);
            var anchorEnd = currentMonth.AddMonths(1).AddDays(-1);
            var lastProjectedMonth = currentMonth.AddMonths(6);
            var lastDay = new DateOnly(
                lastProjectedMonth.Year,
                lastProjectedMonth.Month,
                DateTime.DaysInMonth(lastProjectedMonth.Year, lastProjectedMonth.Month));
            var firstLoadedDay = calendarMonth < currentMonth ? calendarMonth : currentMonth;
            var ledgerEntries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.DueDate >= firstLoadedDay &&
                    entry.DueDate <= lastDay)
                .ToListAsync(ct);
            var concepts = await _db.Concepts
                .AsNoTracking()
                .Where(concept => concept.UserId == userId && concept.IsActive)
                .ToListAsync(ct);
            var monthlyBudgets = await _db.MonthlyBudgets
                .AsNoTracking()
                .Where(budget =>
                    budget.UserId == userId &&
                    (budget.Year > currentMonth.Year ||
                     (budget.Year == currentMonth.Year && budget.Month >= currentMonth.Month)) &&
                    (budget.Year < lastProjectedMonth.Year ||
                     (budget.Year == lastProjectedMonth.Year && budget.Month <= lastProjectedMonth.Month)))
                .ToListAsync(ct);
            var paidEntries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.Status == EntryStatus.Paid &&
                    entry.ValueDate != null &&
                    entry.ValueDate <= today)
                .ToListAsync(ct);
            var recurringRules = await _db.RecurringRules
                .AsNoTracking()
                .Where(rule => rule.UserId == userId && rule.IsActive)
                .ToListAsync(ct);

            var projections = new List<MonthlyProjectionDto>();
            var alerts = new DashboardAlertsDto();
            var culture = new CultureInfo("es-ES");
            var accountBalances = accounts.Select(account => new DashboardAccountBalanceDto
            {
                AccountId = account.Id,
                Name = account.Name,
                Currency = account.Currency,
                Balance = AccountBalanceCalculator.ComputeBalance(account, paidEntries, today)
            }).ToList();
            var currentBalance = accountBalances.Sum(account => account.Balance);
            var previousMonthEnd = currentMonth.AddDays(-1);
            var monthOpeningBalance = accounts.Sum(account => account.OpeningDate > previousMonthEnd
                ? account.OpeningBalance
                : AccountBalanceCalculator.ComputeBalance(account, paidEntries, previousMonthEnd));

            var conceptById = concepts.ToDictionary(concept => concept.Id);
            var variableExpenseConcepts = concepts
                .Where(concept => concept.Kind == ConceptKind.Expense && concept.Nature == ConceptNature.Variable)
                .ToList();
            var variableIncomeConcepts = concepts
                .Where(concept => concept.Kind == ConceptKind.Income && concept.Nature == ConceptNature.Variable)
                .ToList();

            for (int i = 0; i <= 6; i++)
            {
                var target = currentMonth.AddMonths(i);
                var year = target.Year;
                var month = target.Month;
                var isCurrent = i == 0;
                var monthEntries = ledgerEntries
                    .Where(entry =>
                        entry.Status != EntryStatus.Skipped &&
                        !entry.IsTransfer &&
                        entry.DueDate.Year == year &&
                        entry.DueDate.Month == month)
                    .ToList();
                var ledgerTotals = DashboardLedgerCalculator.BreakdownForMonth(ledgerEntries, year, month);
                var materializedRuleIds = ledgerEntries
                    .Where(entry =>
                        entry.DueDate.Year == year &&
                        entry.DueDate.Month == month &&
                        entry.RecurringRuleId != null)
                    .Select(entry => entry.RecurringRuleId!.Value)
                    .ToHashSet();
                var missingRules = recurringRules.Where(rule =>
                    !materializedRuleIds.Contains(rule.Id) &&
                    RecurrenceCalculator.OccursIn(rule, year, month));
                var recurringIncome = missingRules
                    .Where(rule => rule.Direction == EntryDirection.In)
                    .Sum(rule => rule.ForecastAmount);
                var recurringExpense = missingRules
                    .Where(rule => rule.Direction == EntryDirection.Out)
                    .Sum(rule => rule.ForecastAmount);

                var fixedIncome = monthEntries
                    .Where(entry =>
                        entry.Direction == EntryDirection.In &&
                        (!conceptById.TryGetValue(entry.ConceptId, out var concept) ||
                         concept.Nature == ConceptNature.Fixed))
                    .Sum(entry => entry.Status == EntryStatus.Paid
                        ? entry.ActualAmount ?? entry.ForecastAmount
                        : entry.ForecastAmount);
                var variableIncome = variableIncomeConcepts.Sum(concept =>
                {
                    var planned = monthEntries
                        .Where(entry =>
                            entry.Direction == EntryDirection.In &&
                            entry.ConceptId == concept.Id)
                        .Sum(entry => entry.Status == EntryStatus.Paid
                            ? entry.ActualAmount ?? entry.ForecastAmount
                            : entry.ForecastAmount);
                    var overrideEstimate = monthlyBudgets.FirstOrDefault(budget =>
                        budget.ConceptId == concept.Id &&
                        budget.Year == year &&
                        budget.Month == month)?.LimitAmount;
                    var estimate = overrideEstimate ?? concept.DefaultMonthlyBudget ?? 0m;
                    return Math.Max(planned, estimate);
                });
                var income = fixedIncome + recurringIncome + variableIncome;
                var fixedExpense = monthEntries
                    .Where(entry =>
                        entry.Direction == EntryDirection.Out &&
                        (!conceptById.TryGetValue(entry.ConceptId, out var concept) ||
                         concept.Nature == ConceptNature.Fixed))
                    .Sum(entry => entry.Status == EntryStatus.Paid
                        ? entry.ActualAmount ?? entry.ForecastAmount
                        : entry.ForecastAmount);
                var variableExpense = variableExpenseConcepts.Sum(concept =>
                {
                    var planned = monthEntries
                        .Where(entry =>
                            entry.Direction == EntryDirection.Out &&
                            entry.ConceptId == concept.Id)
                        .Sum(entry => entry.Status == EntryStatus.Paid
                            ? entry.ActualAmount ?? entry.ForecastAmount
                            : entry.ForecastAmount);
                    var overrideBudget = monthlyBudgets.FirstOrDefault(budget =>
                        budget.ConceptId == concept.Id &&
                        budget.Year == year &&
                        budget.Month == month)?.LimitAmount;
                    var limit = overrideBudget ?? concept.DefaultMonthlyBudget ?? 0m;
                    return Math.Max(planned, limit);
                });
                var expense = fixedExpense + recurringExpense + variableExpense;
                var source = income == 0m && expense == 0m
                    ? "Sin planificación"
                    : "Tus ingresos y gastos planificados";

                var balance = income - expense;

                // ---------- COMPROMISOS ----------
                var commitmentStatus =
                    await _commitmentMatchingService.GetMonthlyStatusAsync(year, month, ct);

                if (isCurrent && commitmentStatus.Any(c => c.IsOutOfRange))
                {
                    alerts.Items.Add(new AlertItemDto
                    {
                        Type = "Commitment",
                        Message = "Tienes compromisos fuera de rango este mes",
                        Action = "/commitments"
                    });
                }

                // ---------- PRESUPUESTOS ----------
                var budgets = await _budgetService.GetForMonthAsync(year, month, ct);

                if (isCurrent && budgets.Any())
                {
                    alerts.Items.Add(new AlertItemDto
                    {
                        Type = "Budget",
                        Message = "Revisa tus presupuestos: algunos pueden estar cerca del límite",
                        Action = "/budgets"
                    });
                }

                projections.Add(new MonthlyProjectionDto
                {
                    Month = target.ToString("MMMM yyyy", culture),
                    Year = year,
                    MonthNumber = month,
                    Income = income,
                    Expense = expense,
                    Balance = balance,
                    IsCurrent = isCurrent,
                    ProjectionSource = source,
                    PendingIncome = ledgerTotals.PendingIncome + recurringIncome,
                    PendingExpense = ledgerTotals.PendingExpense + recurringExpense
                });
            }

            alerts.HasCriticalAlerts = alerts.Items.Any(a => a.Type != "Info");

            // El resumen describe el mismo mes que el resto del Dashboard, no el del calendario.
            var anchorTotals = DashboardLedgerCalculator
                .BreakdownForMonth(ledgerEntries, currentMonth.Year, currentMonth.Month);
            var summary = new SummaryDto
            {
                CurrentBalance = currentBalance,
                MonthOpeningBalance = monthOpeningBalance,
                CurrentMonthIncome = anchorTotals.ActualIncome,
                CurrentMonthExpense = anchorTotals.ActualExpense
            };
            summary.CurrentMonthResult = summary.CurrentMonthIncome - summary.CurrentMonthExpense;

            var reconciliations = await _db.Reconciliations
                .AsNoTracking()
                .Where(item =>
                    item.UserId == userId &&
                    item.Year == currentMonth.Year &&
                    item.Month == currentMonth.Month)
                .ToListAsync(ct);
            var reconciledBalances = reconciliations
                .GroupBy(item => item.BankId)
                .ToDictionary(
                    group => group.Key,
                    group => group.OrderByDescending(item => item.CreatedAt).First().ClosingBalance);
            var anchorBudgets = monthlyBudgets
                .Where(budget => budget.Year == currentMonth.Year && budget.Month == currentMonth.Month)
                .ToList();

            var outlook = MonthOutlookCalculator.Build(new MonthOutlookInput(
                currentMonth.Year,
                currentMonth.Month,
                today,
                accounts,
                paidEntries,
                ledgerEntries.Where(entry => entry.DueDate <= anchorEnd).ToList(),
                recurringRules,
                concepts,
                anchorBudgets,
                reconciledBalances));

            var period = await _db.MonthlyPeriods
                .AsNoTracking()
                .FirstOrDefaultAsync(item =>
                    item.UserId == userId &&
                    item.Year == currentMonth.Year &&
                    item.Month == currentMonth.Month, ct);

            return new DashboardProjectionResult
            {
                MonthlyData = projections,
                Summary = summary,
                Alerts = alerts,
                Accounts = accountBalances,
                Outlook = outlook,
                Period = new PeriodStateDto
                {
                    Status = period is null
                        ? "notOpened"
                        : period.Status == PeriodStatus.Closed ? "closed" : "open",
                    ClosedAt = period?.ClosedAt,
                    ClosingBalance = period?.ClosingBalance
                },
                DefaultYear = defaultMonth.Year,
                DefaultMonth = defaultMonth.Month,
                MinYear = minMonth.Year,
                MinMonth = minMonth.Month
            };
        }
    }
}
