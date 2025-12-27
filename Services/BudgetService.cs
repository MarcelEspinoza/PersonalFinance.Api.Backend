using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class BudgetService : IBudgetService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public BudgetService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
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

        public async Task<List<Budget>> GetForMonthAsync(
            int year,
            int month,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var target = new DateTime(year, month, 1);

            return await _db.Budgets
                .Include(b => b.Category)
                .Where(b =>
                    b.UserId == userId &&
                    b.IsActive &&
                    b.StartMonth <= target &&
                    (b.EndMonth == null || b.EndMonth >= target))
                .OrderBy(b => b.Category.Name)
                .ToListAsync(ct);
        }

        public async Task<Budget> CreateAsync(
            Budget budget,
            CancellationToken ct = default)
        {
            budget.Id = Guid.NewGuid();
            budget.UserId = CurrentUserId();

            _db.Budgets.Add(budget);
            await _db.SaveChangesAsync(ct);

            return budget;
        }

        public async Task UpdateAsync(
        Guid id,
        Budget updated,
        CancellationToken ct = default)
        {
            var userId = CurrentUserId();

            var entity = await _db.Budgets
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);

            if (entity == null)
                throw new KeyNotFoundException("Presupuesto no encontrado");

            entity.CategoryId = updated.CategoryId;
            entity.MonthlyLimit = updated.MonthlyLimit;
            entity.StartMonth = updated.StartMonth;
            entity.EndMonth = updated.EndMonth;
            entity.IsActive = updated.IsActive;

            await _db.SaveChangesAsync(ct);
        }


        public async Task DisableAsync(
            Guid id,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();

            var entity = await _db.Budgets
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId, ct);

            if (entity == null)
                throw new KeyNotFoundException();

            entity.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }
}
