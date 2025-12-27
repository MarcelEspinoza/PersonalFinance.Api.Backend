using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Budgets;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class BudgetMatchingService : IBudgetMatchingService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public BudgetMatchingService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private Guid CurrentUserId()
        {
            var id = _http.HttpContext?
                .User?
                .FindFirst(ClaimTypes.NameIdentifier)?
                .Value;

            if (!Guid.TryParse(id, out var userId))
                throw new UnauthorizedAccessException();

            return userId;
        }

        public async Task<List<BudgetStatusDto>> GetMonthlyStatusAsync(
            int year,
            int month,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddTicks(-1);

            var budgets = await _db.Budgets
                .Include(b => b.Category)
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    b.StartMonth <= start &&
                    (b.EndMonth == null || b.EndMonth >= start))
                .ToListAsync(ct);

            var expenses = await _db.Expenses
                .Where(e =>
                    e.UserId == userId &&
                    e.Date >= start &&
                    e.Date <= end &&
                    !e.IsTransfer)
                .ToListAsync(ct);

            var result = new List<BudgetStatusDto>();

            foreach (var b in budgets)
            {
                var spent = expenses
                    .Where(e => e.CategoryId == b.CategoryId)
                    .Sum(e => e.Amount);

                result.Add(new BudgetStatusDto
                {
                    BudgetId = b.Id,
                    CategoryId = b.CategoryId,
                    CategoryName = b.Category.Name,
                    MonthlyLimit = b.MonthlyLimit,
                    SpentAmount = spent
                });
            }

            return result;
        }
    }
}
