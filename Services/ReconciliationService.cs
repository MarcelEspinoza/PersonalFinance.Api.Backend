
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class ReconciliationService : IReconciliationService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public ReconciliationService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private Guid CurrentUserId()
        {
            var uid = _http.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(uid, out var g) ? g : Guid.Empty;
        }

        public async Task<IEnumerable<Reconciliation>> GetForMonthAsync(int year, int month, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            return await _db.Set<Reconciliation>().Where(r => r.UserId == userId && r.Year == year && r.Month == month).OrderByDescending(r => r.CreatedAt).ToListAsync(ct);
        }

        public async Task<Reconciliation?> CreateAsync(CreateReconciliationDto dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            // avoid duplicates: update if exists for same bank/year/month
            var existing = await _db.Set<Reconciliation>().FirstOrDefaultAsync(r => r.UserId == userId && r.BankId == dto.BankId && r.Year == dto.Year && r.Month == dto.Month, ct);
            if (existing != null)
            {
                existing.ClosingBalance = dto.ClosingBalance;
                existing.Notes = dto.Notes;
                existing.Reconciled = false;
                existing.ReconciledAt = null;
                await _db.SaveChangesAsync(ct);
                return existing;
            }

            var rec = new Reconciliation
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                BankId = dto.BankId,
                Year = dto.Year,
                Month = dto.Month,
                ClosingBalance = dto.ClosingBalance,
                Notes = dto.Notes,
                CreatedAt = DateTime.UtcNow
            };
            _db.Add(rec);
            await _db.SaveChangesAsync(ct);
            return rec;
        }

        public async Task<ReconciliationSuggestionDto> SuggestAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);

            // Adjust the queries below to your actual Income/Expense entities.
            // If your Incomes/Expenses are in a single Transaction table, adapt accordingly.
            decimal incomeTotal = 0m;
            decimal expenseTotal = 0m;

            // Safe check: if tables exist in DbContext
            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                incomeTotal = await _db.Set<Income>()
                    .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end && (bankId == null || i.BankId == bankId))
                    .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                expenseTotal = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end && (bankId == null || e.BankId == bankId))
                    .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            }

            var systemTotal = incomeTotal - expenseTotal;

            Reconciliation? saved = null;
            if (bankId != null)
            {
                saved = await _db.Set<Reconciliation>().FirstOrDefaultAsync(r => r.UserId == userId && r.BankId == bankId && r.Year == year && r.Month == month, ct);
            }

            var suggestion = new ReconciliationSuggestionDto(SystemTotal: systemTotal, ClosingBalance: saved?.ClosingBalance ?? 0m, Difference: (saved?.ClosingBalance ?? 0m) - systemTotal, Details: new { incomeTotal, expenseTotal });
            return suggestion;
        }

        public async Task<bool> MarkReconciledAsync(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var rec = await _db.Set<Reconciliation>().FindAsync(new object[] { id }, ct);
            if (rec == null || rec.UserId != userId) return false;
            rec.Reconciled = true;
            rec.ReconciledAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}

