using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Commitments;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class CommitmentMatchingService : ICommitmentMatchingService
    {
        private readonly AppDbContext _db;
        private readonly ICommitmentService _commitments;
        private readonly IHttpContextAccessor _http;

        public CommitmentMatchingService(
            AppDbContext db,
            ICommitmentService commitments,
            IHttpContextAccessor http)
        {
            _db = db;
            _commitments = commitments;
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

        public async Task<List<CommitmentStatusDto>> GetMonthlyStatusAsync(
            int year,
            int month,
            CancellationToken ct = default)
        {
            if (month < 1 || month > 12)
                throw new ArgumentException("Mes inválido");

            if (year < 1900 || year > 2100)
                throw new ArgumentException("Año inválido");

            var userId = CurrentUserId();

            var start = new DateTime(year, month, 1);
            var end = start.AddMonths(1).AddTicks(-1);

            // 👇 compromisos activos del mes (ya filtra por usuario internamente)
            var commitments = await _commitments.GetForMonthAsync(year, month, ct);

            var incomes = await _db.Incomes
                .Where(i =>
                    i.UserId == userId &&
                    i.Date >= start &&
                    i.Date <= end &&
                    !i.IsTransfer)
                .ToListAsync(ct);

            var expenses = await _db.Expenses
                .Where(e =>
                    e.UserId == userId &&
                    e.Date >= start &&
                    e.Date <= end &&
                    !e.IsTransfer)
                .ToListAsync(ct);

            var result = new List<CommitmentStatusDto>();

            foreach (var c in commitments)
            {
                decimal actual = 0m;

                if (c.Type == "Income")
                {
                    actual = incomes
                        .Where(i =>
                            (!c.CategoryId.HasValue || i.CategoryId == c.CategoryId) &&
                            (!c.BankId.HasValue || i.BankId == c.BankId))
                        .Sum(i => i.Amount);
                }
                else // Expense
                {
                    actual = expenses
                        .Where(e =>
                            (!c.CategoryId.HasValue || e.CategoryId == c.CategoryId) &&
                            (!c.BankId.HasValue || e.BankId == c.BankId))
                        .Sum(e => e.Amount);
                }

                var min = c.ExpectedAmount - c.Tolerance;
                var max = c.ExpectedAmount + c.Tolerance;

                result.Add(new CommitmentStatusDto
                {
                    CommitmentId = c.Id,
                    Name = c.Name,
                    ExpectedAmount = c.ExpectedAmount,
                    ActualAmount = actual,
                    IsSatisfied = actual >= min && actual <= max,
                    IsOutOfRange = actual < min || actual > max
                });
            }

            return result;
        }
    }
}
