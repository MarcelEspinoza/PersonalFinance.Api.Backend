using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services.Contracts;
using System.Globalization;

public class DashboardService : IDashboardService
{
    private readonly AppDbContext _context;

    public DashboardService(AppDbContext context)
    {
        _context = context;
    }

    public async Task<(List<MonthlyProjectionDto> monthlyData, SummaryDto summary)> GetFutureProjectionAsync(Guid userId)
    {
        var incomes = await _context.Incomes
            .Where(i => i.UserId == userId)
            .ToListAsync();

        var expenses = await _context.Expenses
            .Where(e => e.UserId == userId)
            .ToListAsync();

        var grouped = incomes.Select(i => new { i.Date.Year, i.Date.Month, Amount = i.Amount, Type = "Income" })
            .Concat(expenses.Select(e => new { e.Date.Year, e.Date.Month, Amount = e.Amount, Type = "Expense" }))
            .GroupBy(t => new { t.Year, t.Month })
            .Select(g =>
            {
                var income = g.Where(x => x.Type == "Income").Sum(x => x.Amount);
                var expense = g.Where(x => x.Type == "Expense").Sum(x => x.Amount);

                return new
                {
                    g.Key.Year,
                    g.Key.Month,
                    Income = income,
                    Expense = expense,
                    Balance = income - expense
                };
            })
            .ToList();

        var projections = new List<MonthlyProjectionDto>();
        for (int i = 0; i <= 6; i++)
        {
            var targetDate = DateTime.UtcNow.AddMonths(i);
            var existing = grouped.FirstOrDefault(g => g.Year == targetDate.Year && g.Month == targetDate.Month);

            projections.Add(new MonthlyProjectionDto
            {
                Month = targetDate.ToString("MMMM yyyy", new CultureInfo("es-ES")),
                Income = existing?.Income ?? 0,
                Expense = existing?.Expense ?? 0,
                Balance = existing?.Balance ?? 0,
                IsCurrent = i == 0
            });
        }

        var totalIncome = projections.Sum(m => m.Income);
        var totalExpense = projections.Sum(m => m.Expense);
        var balanceTotal = totalIncome - totalExpense;

        var summary = new SummaryDto
        {
            TotalIncome = totalIncome,
            TotalExpense = totalExpense,
            Balance = balanceTotal,
            Savings = balanceTotal * 0.2m
        };

        return (projections, summary);
    }

}
