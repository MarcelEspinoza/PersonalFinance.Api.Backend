using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
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
            DashboardAlertsDto alerts
        )> GetFutureProjectionAsync(CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var now = DateTime.UtcNow;

            var incomes = await _db.Incomes
                .Where(i => i.UserId == userId && !i.IsTransfer)
                .ToListAsync(ct);

            var expenses = await _db.Expenses
                .Where(e => e.UserId == userId && !e.IsTransfer)
                .ToListAsync(ct);

            var projections = new List<MonthlyProjectionDto>();
            var alerts = new DashboardAlertsDto();
            var culture = new CultureInfo("es-ES");

            for (int i = 0; i <= 6; i++)
            {
                var target = now.AddMonths(i);
                var year = target.Year;
                var month = target.Month;
                var isCurrent = i == 0;

                var monthIncomes = incomes
                    .Where(i => i.Date.Year == year && i.Date.Month == month)
                    .Sum(i => i.Amount);

                var monthExpenses = expenses
                    .Where(e =>
                        e.Date.Year == year &&
                        e.Date.Month == month &&
                        !(e.Type == "Temporary" && e.CategoryId == DefaultCategories.Savings))
                    .Sum(e => e.Amount);

                var balance = monthIncomes - monthExpenses;

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
                if (!isCurrent && balance < 0)
                {
                    alerts.Items.Add(new AlertItemDto
                    {
                        Type = "Balance",
                        Message = $"Balance negativo previsto en {target.ToString("MMMM", culture)}",
                        Action = "/dashboard"
                    });
                }

                projections.Add(new MonthlyProjectionDto
                {
                    Month = target.ToString("MMMM yyyy", culture),
                    Income = monthIncomes,
                    Expense = monthExpenses,
                    Balance = balance,
                    IsCurrent = isCurrent
                });
            }

            alerts.HasCriticalAlerts = alerts.Items.Any(a => a.Type != "Info");

            var summary = new SummaryDto
            {
                TotalIncome = projections.Sum(p => p.Income),
                TotalExpense = projections.Sum(p => p.Expense),
                Balance = projections.Sum(p => p.Balance)
            };

            return (projections, summary, alerts);
        }
    }
}
