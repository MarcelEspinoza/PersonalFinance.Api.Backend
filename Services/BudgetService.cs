using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Extensions;
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
            var userId = CurrentUserId();
            await EnsureCategoryAsync(userId, budget.CategoryId, ct);

            var entity = new Budget
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                CategoryId = budget.CategoryId,
                MonthlyLimit = budget.MonthlyLimit,
                StartMonth = budget.StartMonth,
                EndMonth = budget.EndMonth,
                IsActive = budget.IsActive
            };

            _db.Budgets.Add(entity);
            await _db.SaveChangesAsync(ct);

            return entity;
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
                throw new NotFoundException("Presupuesto no encontrado");

            await EnsureCategoryAsync(userId, updated.CategoryId, ct);

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
                throw new NotFoundException("Presupuesto no encontrado");

            entity.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }

        private async Task EnsureCategoryAsync(Guid userId, int categoryId, CancellationToken ct)
        {
            if (!await _db.IsCategoryVisibleAsync(userId, categoryId, ct))
                throw new BusinessRuleException("La categoría no existe.");
        }
    }
}
