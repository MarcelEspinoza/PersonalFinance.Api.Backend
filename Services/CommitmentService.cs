using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class CommitmentService : ICommitmentService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public CommitmentService(AppDbContext db, IHttpContextAccessor http)
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

        public async Task<List<FinancialCommitment>> GetForMonthAsync(
            int year,
            int month,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var target = new DateTime(year, month, 1);

            return await _db.FinancialCommitments
                .Where(c =>
                    c.UserId == userId &&
                    c.IsActive &&
                    c.StartMonth <= target &&
                    (c.EndMonth == null || c.EndMonth >= target))
                .OrderBy(c => c.Type)
                .ThenBy(c => c.Name)
                .ToListAsync(ct);
        }

        public async Task<FinancialCommitment> CreateAsync(
            FinancialCommitment commitment,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            await EnsureReferencesAsync(userId, commitment.CategoryId, commitment.BankId, ct);

            commitment.Id = Guid.NewGuid();
            commitment.UserId = userId;

            _db.FinancialCommitments.Add(commitment);
            await _db.SaveChangesAsync(ct);

            return commitment;
        }

        public async Task UpdateAsync(
            Guid id,
            FinancialCommitment updated,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();

            var entity = await _db.FinancialCommitments
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

            if (entity == null)
                throw new NotFoundException("Compromiso no encontrado");

            await EnsureReferencesAsync(userId, updated.CategoryId, updated.BankId, ct);

            entity.Name = updated.Name;
            entity.Type = updated.Type;
            entity.ExpectedAmount = updated.ExpectedAmount;
            entity.Tolerance = updated.Tolerance;
            entity.StartMonth = updated.StartMonth;
            entity.EndMonth = updated.EndMonth;
            entity.CategoryId = updated.CategoryId;
            entity.BankId = updated.BankId;

            await _db.SaveChangesAsync(ct);
        }

        public async Task DisableAsync(
            Guid id,
            CancellationToken ct = default)
        {
            var userId = CurrentUserId();

            var entity = await _db.FinancialCommitments
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, ct);

            if (entity == null)
                throw new NotFoundException("Compromiso no encontrado");

            entity.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }

        private async Task EnsureReferencesAsync(Guid userId, int? categoryId, Guid? bankId, CancellationToken ct)
        {
            if (categoryId.HasValue && !await _db.IsCategoryVisibleAsync(userId, categoryId.Value, ct))
                throw new BusinessRuleException("La categoría no existe.");
            if (!await _db.IsBankOwnedOrNullAsync(userId, bankId, ct))
                throw new BusinessRuleException("El banco no existe.");
        }
    }
}
