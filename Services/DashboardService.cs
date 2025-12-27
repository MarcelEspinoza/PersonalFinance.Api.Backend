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
            var id = _http.HttpContext?
                .User?
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;

            if (!Guid.TryParse(id, out var userId))
                throw new UnauthorizedAccessException("Usuario no autenticado");

            return userId;
        }

        public async Task<(List<MonthlyProjectionDto> monthlyData, SummaryDto summary)>
            GetFutureProjectionAsync(CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var now = DateTime.UtcNow;

            var incomes = await _db.Incomes
                .Where(i => i.UserId == userId && !i.IsTransfer)
                .ToListAsync(ct);

            var expenses = await _db.Expenses
                .Where(e => e.UserId == userId && !e.IsTransfer)
                .ToListAsync(ct);

            var currentMonthRealSavings =
                await _savingService.GetSavingsForMonthAsync(userId, now);

            var projections = new List<MonthlyProjectionDto>();
            var culture = new CultureInfo("es-ES");

            for (int i = 0; i <= 6; i++)
            {
                var target = now.AddMonths(i);
                var year = target.Year;
                var month = target.Month;
                var isCurrent = i == 0;

                // ================= REAL =================
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

                // ================= COMPROMISOS =================
                var commitments = await _commitmentService.GetForMonthAsync(year, month, ct);

                var committedIncome = commitments
                    .Where(c => c.Type == "Income")
                    .Sum(c => c.ExpectedAmount);

                var committedExpense = commitments
                    .Where(c => c.Type == "Expense")
                    .Sum(c => c.ExpectedAmount);

                // ================= MATCHING (estado) =================
                var commitmentStatus =
                    await _commitmentMatchingService.GetMonthlyStatusAsync(year, month, ct);

                var commitmentSummary = new CommitmentSummaryDto
                {
                    Total = commitmentStatus.Count,
                    Ok = commitmentStatus.Count(c => c.IsSatisfied),
                    Pending = commitmentStatus.Count(c => c.ActualAmount == 0),
                    OutOfRange = commitmentStatus.Count(c => c.IsOutOfRange)
                };

                // ================= PRESUPUESTOS =================
                var budgets = await _budgetService.GetForMonthAsync(year, month, ct);
                var budgetedExpenses = budgets.Sum(b => b.MonthlyLimit);

                // ================= AHORRO =================
                var savingsReal = isCurrent ? currentMonthRealSavings : 0m;

                var projectedSavings =
                    isCurrent
                        ? 0m
                        : Math.Max(0, (monthIncomes - monthExpenses) * 0.2m);

                var plannedBalance =
                    (committedIncome - committedExpense - budgetedExpenses) - projectedSavings;

                projections.Add(new MonthlyProjectionDto
                {
                    Month = target.ToString("MMMM yyyy", culture),
                    Income = monthIncomes,
                    Expense = monthExpenses,
                    Balance = balance,
                    IsCurrent = isCurrent,
                    Savings = savingsReal,
                    ProjectedSavings = projectedSavings,
                    PlannedBalance = plannedBalance,
                    Commitments = commitmentSummary
                });
            }

            var summary = new SummaryDto
            {
                TotalIncome = projections.Sum(p => p.Income),
                TotalExpense = projections.Sum(p => p.Expense),
                Balance = projections.Sum(p => p.Balance),
                Savings = projections.Where(p => p.IsCurrent).Sum(p => p.Savings),
                ProjectedSavings = projections.Where(p => !p.IsCurrent).Sum(p => p.ProjectedSavings),
                PlannedBalance = projections.Sum(p => p.PlannedBalance ?? 0m)
            };

            return (projections, summary);
        }
    }
}
