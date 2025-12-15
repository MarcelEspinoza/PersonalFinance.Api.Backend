using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Monthly;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class AnalyticsService: IAnalyticsService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public AnalyticsService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private Guid CurrentUserId()
        {
            var id = _http.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(id, out var g) ? g : Guid.Empty;
        }

        public async Task<MonthlyInsightsDto> GetMonthlyAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default)
        {
            if (month < 1 || month > 12) throw new ArgumentException("month inválido");
            if (year < 1900 || year > 2100) throw new ArgumentException("year inválido");

            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);
            var userId = CurrentUserId();

            // ============ INCOMES ============
            var incomesQuery = _db.Incomes
                .Include(i => i.Category)
                .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end && !i.IsTransfer);

            // ============ EXPENSES ============
            var expensesQuery = _db.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end && !e.IsTransfer);

            if (bankId.HasValue)
            {
                incomesQuery = incomesQuery.Where(i => i.BankId == bankId);
                expensesQuery = expensesQuery.Where(e => e.BankId == bankId);
            }

            var incomes = await incomesQuery.ToListAsync(ct);
            var expenses = await expensesQuery.ToListAsync(ct);

            var dto = new MonthlyInsightsDto
            {
                Year = year,
                Month = month,
                Currency = "EUR",
                TotalIncomes = incomes.Sum(i => i.Amount),
                TotalExpenses = expenses.Sum(e => e.Amount),
                TxCount = incomes.Count + expenses.Count
            };

            // ============ GASTO POR CATEGORÍA ============
            var catTotals = expenses
                .GroupBy(e => e.CategoryId)
                .Select(g => new
                {
                    CategoryId = g.Key,
                    CategoryName = g.First().Category != null ? g.First().Category.Name : "Sin categoría",
                    Amount = g.Sum(x => x.Amount)
                })
                .OrderByDescending(x => x.Amount)
                .ToList();

            var totalExpenses = catTotals.Sum(c => c.Amount);
            dto.ByCategory = catTotals.Select(c => new MonthlyInsightsDto.CategorySummary
            {
                CategoryId = c.CategoryId,
                CategoryName = c.CategoryName,
                Amount = c.Amount,
                Pct = totalExpenses > 0 ? c.Amount / totalExpenses : 0
            }).ToList();

            // ============ TOP 5 GASTOS ============
            dto.TopExpenses = expenses
                .OrderByDescending(e => e.Amount)
                .Take(5)
                .Select(e => new MonthlyInsightsDto.TransactionSummary
                {
                    Id = e.Id,
                    Description = e.Description,
                    Amount = e.Amount,
                    Date = e.Date,
                    CategoryName = e.Category?.Name
                }).ToList();

            // ============ TOP 5 INGRESOS ============
            dto.TopIncomes = incomes
                .OrderByDescending(i => i.Amount)
                .Take(5)
                .Select(i => new MonthlyInsightsDto.TransactionSummary
                {
                    Id = i.Id,
                    Description = i.Description,
                    Amount = i.Amount,
                    Date = i.Date,
                    CategoryName = i.Category?.Name
                }).ToList();

            // ============ MAYOR INGRESO ============
            var largestIncome = incomes.OrderByDescending(i => i.Amount).FirstOrDefault();
            if (largestIncome != null)
            {
                dto.LargestIncome = new MonthlyInsightsDto.TransactionSummary
                {
                    Id = largestIncome.Id,
                    Description = largestIncome.Description,
                    Amount = largestIncome.Amount,
                    Date = largestIncome.Date,
                    CategoryName = largestIncome.Category?.Name
                };
            }

            // ============ KPIs ADICIONALES ============
            var daysWithSpend = expenses.Select(e => e.Date.Date).Distinct().Count();
            dto.DaysWithSpend = daysWithSpend;
            dto.AvgDailySpend = daysWithSpend > 0 ? dto.TotalExpenses / daysWithSpend : 0;

            return dto;
        }
    }
}
