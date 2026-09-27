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

        public async Task<(
            List<MonthlyProjectionDto> monthlyData,
            SummaryDto summary,
            DashboardAlertsDto alerts,
            List<DashboardAccountBalanceDto> accounts
        )> GetFutureProjectionAsync(CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var today = DateOnly.FromDateTime(DateTime.UtcNow);
            var currentMonth = new DateOnly(today.Year, today.Month, 1);
            var historyStart = currentMonth.AddMonths(-3);
            var lastProjectedMonth = currentMonth.AddMonths(6);
            var lastDay = new DateOnly(
                lastProjectedMonth.Year,
                lastProjectedMonth.Month,
                DateTime.DaysInMonth(lastProjectedMonth.Year, lastProjectedMonth.Month));
            var ledgerEntries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(entry =>
                    entry.UserId == userId &&
                    entry.DueDate >= historyStart &&
                    entry.DueDate <= lastDay)
                .ToListAsync(ct);
            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(account => account.UserId == userId && account.IsActive)
                .OrderBy(account => account.Name)
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
            var monthOpeningBalance = accounts.Sum(account =>
                AccountBalanceCalculator.ComputeBalance(account, paidEntries, previousMonthEnd));

            var historical = Enumerable.Range(1, 3)
                .Select(offset =>
                {
                    var month = currentMonth.AddMonths(-offset);
                    return DashboardLedgerCalculator.BreakdownForMonth(
                        ledgerEntries, month.Year, month.Month);
                })
                .ToList();
            var historicalIncome = historical.Count == 0 ? 0m : historical.Average(item => item.ActualIncome);
            var historicalExpense = historical.Count == 0 ? 0m : historical.Average(item => item.ActualExpense);
            var projectedClosingBalance = currentBalance;
            var negativeBalanceAlertAdded = false;

            for (int i = 0; i <= 6; i++)
            {
                var target = currentMonth.AddMonths(i);
                var year = target.Year;
                var month = target.Month;
                var isCurrent = i == 0;
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

                decimal income;
                decimal expense;
                bool isEstimate;
                string source;

                if (isCurrent)
                {
                    income = ledgerTotals.ActualIncome + ledgerTotals.PendingIncome + recurringIncome;
                    expense = ledgerTotals.ActualExpense + ledgerTotals.PendingExpense + recurringExpense;
                    isEstimate = ledgerTotals.PendingIncome + ledgerTotals.PendingExpense + recurringIncome + recurringExpense > 0;
                    source = isEstimate ? "Real hasta hoy + pendiente" : "Real hasta hoy";
                    projectedClosingBalance += ledgerTotals.PendingIncome + recurringIncome
                        - ledgerTotals.PendingExpense - recurringExpense;
                }
                else
                {
                    income = ledgerTotals.ActualIncome + ledgerTotals.PendingIncome + recurringIncome;
                    expense = ledgerTotals.ActualExpense + ledgerTotals.PendingExpense + recurringExpense;
                    var estimatedIncome = income == 0m;
                    var estimatedExpense = expense == 0m;
                    isEstimate = estimatedIncome || estimatedExpense;
                    if (estimatedIncome) income = historicalIncome;
                    if (estimatedExpense) expense = historicalExpense;

                    if (estimatedIncome && estimatedExpense)
                    {
                        source = "Media real de los últimos 3 meses";
                    }
                    else if (isEstimate)
                    {
                        source = "Planificación completada con media histórica";
                    }
                    else
                    {
                        source = "Planificación y movimientos previstos";
                    }

                    projectedClosingBalance += income - expense;
                }

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

                // ---------- BALANCE FUTURO ----------
                if (!isCurrent && projectedClosingBalance < 0 && !negativeBalanceAlertAdded)
                {
                    alerts.Items.Add(new AlertItemDto
                    {
                        Type = "Balance",
                        Message = $"El saldo disponible podría ser negativo en {target.ToString("MMMM", culture)}",
                        Action = "/dashboard"
                    });
                    negativeBalanceAlertAdded = true;
                }

                projections.Add(new MonthlyProjectionDto
                {
                    Month = target.ToString("MMMM yyyy", culture),
                    Year = year,
                    MonthNumber = month,
                    Income = income,
                    Expense = expense,
                    Balance = balance,
                    ClosingBalance = projectedClosingBalance,
                    IsCurrent = isCurrent,
                    IsEstimate = isEstimate,
                    ProjectionSource = source,
                    PendingIncome = ledgerTotals.PendingIncome + recurringIncome,
                    PendingExpense = ledgerTotals.PendingExpense + recurringExpense
                });
            }

            alerts.HasCriticalAlerts = alerts.Items.Any(a => a.Type != "Info");

            var summary = new SummaryDto
            {
                CurrentBalance = currentBalance,
                MonthOpeningBalance = monthOpeningBalance,
                CurrentMonthIncome = DashboardLedgerCalculator
                    .BreakdownForMonth(ledgerEntries, today.Year, today.Month).ActualIncome,
                CurrentMonthExpense = DashboardLedgerCalculator
                    .BreakdownForMonth(ledgerEntries, today.Year, today.Month).ActualExpense,
                ProjectedBalance = projections[^1].ClosingBalance
            };
            summary.CurrentMonthResult = summary.CurrentMonthIncome - summary.CurrentMonthExpense;
            summary.ProjectionChange = summary.ProjectedBalance - summary.CurrentBalance;

            return (projections, summary, alerts, accountBalances);
        }
    }
}
