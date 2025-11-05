using PersonalFinance.Api.Models;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
using System.Globalization;

public class DashboardService : IDashboardService
{
    private readonly IIncomeService _incomeService;
    private readonly IExpenseService _expenseService;
    private readonly ISavingService _savingService;

    public DashboardService(
        IIncomeService incomeService,
        IExpenseService expenseService,
        ISavingService savingService)
    {
        _incomeService = incomeService;
        _expenseService = expenseService;
        _savingService = savingService;
    }

    public async Task<(List<MonthlyProjectionDto> monthlyData, SummaryDto summary)>
        GetFutureProjectionAsync(Guid userId)
    {
        var now = DateTime.UtcNow;
        var incomes = await _incomeService.GetAllAsync(userId);
        var expenses = await _expenseService.GetAllAsync(userId);

        // Ahorro real del mes actual
        var currentMonthRealSavings = await _savingService.GetSavingsForMonthAsync(userId, now);

        // Agrupación por mes (excluyendo gastos temporales de ahorro)
        var grouped = incomes.Select(i => new { i.Date.Year, i.Date.Month, Amount = i.Amount, Type = "Income" })
            .Concat(expenses
                .Where(e => !(e.Type == "Temporary" && e.CategoryId == DefaultCategories.Savings))
                .Select(e => new { e.Date.Year, e.Date.Month, Amount = e.Amount, Type = "Expense" }))
            .GroupBy(t => new { t.Year, t.Month })
            .Select(g => new
            {
                g.Key.Year,
                g.Key.Month,
                Income = g.Where(x => x.Type == "Income").Sum(x => x.Amount),
                Expense = g.Where(x => x.Type == "Expense").Sum(x => x.Amount)
            })
            .ToList();

        decimal ProjectSavingsFromBalance(decimal balance) => balance > 0 ? Math.Round(balance * 0.2m, 2) : 0m;

        var projections = new List<MonthlyProjectionDto>();
        var culture = new CultureInfo("es-ES");

        for (int i = 0; i <= 6; i++)
        {
            var targetDate = now.AddMonths(i);
            var existing = grouped.FirstOrDefault(g => g.Year == targetDate.Year && g.Month == targetDate.Month);

            var income = existing?.Income ?? 0m;
            var expense = existing?.Expense ?? 0m;
            var balance = income - expense;
            var isCurrent = i == 0;

            var savingsReal = isCurrent ? currentMonthRealSavings : 0m;

            // Buscar ahorro proyectado en gastos temporales
            var tempSavings = expenses
                .Where(e => e.UserId == userId
                         && e.CategoryId == DefaultCategories.Savings
                         && e.Type == "Temporary"
                         && e.Date.Year == targetDate.Year
                         && e.Date.Month == targetDate.Month)
                .Sum(e => e.Amount);

            var projectedSavings = tempSavings > 0 ? tempSavings : (isCurrent ? 0m : ProjectSavingsFromBalance(balance));

            // Balance neto planificado (opcional)
            var plannedBalance = balance - projectedSavings;

            projections.Add(new MonthlyProjectionDto
            {
                Month = targetDate.ToString("MMMM yyyy", culture),
                Income = income,
                Expense = expense,
                Balance = balance,                // operacional
                IsCurrent = isCurrent,
                Savings = savingsReal,            // real
                ProjectedSavings = projectedSavings,
                PlannedBalance = plannedBalance    // 👈 nuevo campo
            });
        }

        var summary = new SummaryDto
        {
            TotalIncome = projections.Sum(m => m.Income),
            TotalExpense = projections.Sum(m => m.Expense),
            Balance = projections.Sum(m => m.Income) - projections.Sum(m => m.Expense),
            Savings = projections.Where(p => p.IsCurrent).Sum(p => p.Savings),
            ProjectedSavings = projections.Where(p => !p.IsCurrent).Sum(p => p.ProjectedSavings),
            PlannedBalance = projections.Sum(p => p.PlannedBalance ?? 0m)
        };

        return (projections, summary);
    }
}
