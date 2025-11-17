using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Collections.Generic;
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

            // Calculate system totals from Incomes and Expenses, as already done in repo
            decimal incomeTotal = 0m;
            decimal expenseTotal = 0m;

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                incomeTotal = await _db.Set<Income>()
                    .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end)
                    .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                expenseTotal = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end)
                    .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            }

            // FIX: Expenses.Amount in your DB are positive values. The system total should be net (income - expense).
            // Previous code added them (incomeTotal + expenseTotal) which yields the wrong result (sum of absolutes).
            var systemTotal = incomeTotal - expenseTotal;

            // If bankId provided, try to find Reconciliation with that bank/month for closing balance
            decimal closingBalance = 0m;
            if (bankId.HasValue)
            {
                var recon = await _db.Reconciliations
                    .Where(r => r.UserId == userId && r.BankId == bankId.Value && r.Year == year && r.Month == month)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (recon != null)
                    closingBalance = recon.ClosingBalance;
            }
            else
            {
                // If no bank specified, pick the latest reconciliation record for the month (if present)
                var recon = await _db.Reconciliations
                    .Where(r => r.UserId == userId && r.Year == year && r.Month == month)
                    .OrderByDescending(r => r.CreatedAt)
                    .FirstOrDefaultAsync(ct);

                if (recon != null)
                    closingBalance = recon.ClosingBalance;
            }

            var difference = systemTotal - closingBalance;

            // Build actionable suggestions (lightweight heuristics)
            var suggestions = new List<object>(); // will put simple suggestion objects; controller/frontend can interpret

            // We'll suggest: "check incomes/expenses" and also simple candidate groups (by exact match)
            // get all incomes/expenses details (if present) for the month to attempt lightweight matching
            var txList = new List<(int Id, decimal Amount, string Description, DateTime Date)>();

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                var incomes = await _db.Set<Income>()
                    .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end)
                    .Select(i => new { i.Id, i.Amount, i.Description, i.Date })
                    .ToListAsync(ct);
                // incomes are positive amounts
                txList.AddRange(incomes.Select(i => (i.Id, i.Amount, i.Description ?? string.Empty, i.Date)));
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                var expenses = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end)
                    .Select(e => new { e.Id, e.Amount, e.Description, e.Date })
                    .ToListAsync(ct);
                // FIX: treat expense amounts as negative so matching and net calculations are consistent
                txList.AddRange(expenses.Select(e => (e.Id, -e.Amount, e.Description ?? string.Empty, e.Date)));
            }

            // Simple exact match suggestions: amounts that equal the difference (very rare) or if amount close to difference
            // Note: we do NOT return "do the cuadre for me" — we return candidate transactions to check
            foreach (var tx in txList.OrderByDescending(t => Math.Abs((double)t.Amount)))
            {
                // candidate if amount equals difference (very rare) or if amount close to difference
                if (Math.Abs(tx.Amount - difference) <= 0.01m)
                {
                    suggestions.Add(new
                    {
                        Type = "ExactDiff",
                        TransactionId = tx.Id,
                        Amount = tx.Amount,
                        Description = tx.Description,
                        Reason = "Transaction equals system-closing difference"
                    });
                }
            }

            // Also suggest largest transactions (top 5) as candidates to review
            var topTx = txList.OrderByDescending(t => Math.Abs((double)t.Amount)).Take(5)
                .Select(t => new { Type = "TopTx", TransactionId = t.Id, Amount = t.Amount, Description = t.Description })
                .ToList();
            foreach (var s in topTx) suggestions.Add(s);

            // Return a DTO with system totals, closing balance and suggestions in Details (no push to 'cuadre')
            var suggestionDto = new ReconciliationSuggestionDto(systemTotal, closingBalance, difference, suggestions);
            return suggestionDto;
        }

        public async Task<bool> MarkReconciledAsync(Guid id, CancellationToken ct = default)
        {
            // more robust: recalc totals and reject if difference != 0 (with small tolerance)
            var recon = await _db.Reconciliations.FindAsync(new object[] { id }, ct);
            if (recon == null) return false;

            var year = recon.Year;
            var month = recon.Month;
            var userId = recon.UserId;
            var bankId = recon.BankId;

            // Reuse SuggestAsync to compute systemTotal and closingBalance
            var suggestion = await SuggestAsync(year, month, bankId, ct);
            var difference = suggestion.Difference;

            // tolerance: allow very small rounding differences (1 cent)
            var tol = 0.01m;
            if (Math.Abs(difference) > tol)
            {
                return false;
            }

            // Mark as reconciled
            recon.Reconciled = true;
            recon.ReconciledAt = DateTime.UtcNow;
            _db.Reconciliations.Update(recon);
            await _db.SaveChangesAsync(ct);

            return true;
        }
    }
}