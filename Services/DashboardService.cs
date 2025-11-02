using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Dashboard;
using PersonalFinance.Api.Services;
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
            // DashboardService: use server-side grouping and aggregation
        var monthlyIncomeSums = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date < DateTime.UtcNow && t.Type == "income")
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => g.Sum(t => t.Amount))
            .ToListAsync();

        var avgIncome = monthlyIncomeSums.Any() ? monthlyIncomeSums.Average() : 0m;

        var monthlyExpenseSums = await _context.Transactions
            .Where(t => t.UserId == userId && t.Date < DateTime.UtcNow && t.Type == "expense")
            .GroupBy(t => new { t.Date.Year, t.Date.Month })
            .Select(g => g.Sum(t => t.Amount))
            .ToListAsync();

        var avgExpense = monthlyExpenseSums.Any() ? monthlyExpenseSums.Average() : 0m;

        var projections = new List<MonthlyProjectionDto>();

        for (int i = 0; i <= 6; i++) // ← antes era i = 1
        {
            var targetMonth = DateTime.UtcNow.AddMonths(i);
            projections.Add(new MonthlyProjectionDto
            {
                Month = targetMonth.ToString("MMMM yyyy", new CultureInfo("es-ES")),
                Income = Math.Round(avgIncome, 2),
                Expense = Math.Round(avgExpense, 2),
                IsCurrent = i == 0 // ← nuevo campo para destacar el mes actual
            });
        }

        var summary = new SummaryDto
        {
            TotalIncome = avgIncome * 6,
            TotalExpense = avgExpense * 6,
            Balance = (avgIncome - avgExpense) * 6,
            Savings = (avgIncome - avgExpense) * 6 * 0.2m
        };

        return (projections, summary);
    }
}
