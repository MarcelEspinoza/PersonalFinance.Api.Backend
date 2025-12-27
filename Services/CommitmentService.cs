using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
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
            commitment.Id = Guid.NewGuid();
            commitment.UserId = CurrentUserId();

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
                throw new KeyNotFoundException();

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
                throw new KeyNotFoundException();

            entity.IsActive = false;
            await _db.SaveChangesAsync(ct);
        }
    }
}
