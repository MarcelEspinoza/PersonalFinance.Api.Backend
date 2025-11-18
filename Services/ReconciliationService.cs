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
        private readonly ILogger<ReconciliationService> _logger;

        public ReconciliationService(AppDbContext db, IHttpContextAccessor http, ILogger<ReconciliationService> logger)
        {
            _db = db;
            _http = http;
            this._logger = logger;
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

            // Log incoming request for debugging
            _logger?.LogInformation("Reconciliation.CreateAsync called by user {UserId}. Payload: BankId={BankId}, Year={Year}, Month={Month}, ClosingBalance={ClosingBalance}",
                userId, dto.BankId, dto.Year, dto.Month, dto.ClosingBalance);

            // avoid duplicates: update if exists for same bank/year/month
            var existing = await _db.Set<Reconciliation>().FirstOrDefaultAsync(r => r.UserId == userId && r.BankId == dto.BankId && r.Year == dto.Year && r.Month == dto.Month, ct);
            if (existing != null)
            {
                existing.ClosingBalance = dto.ClosingBalance;
                existing.Notes = dto.Notes;
                existing.Reconciled = false;
                existing.ReconciledAt = null;
                await _db.SaveChangesAsync(ct);

                _logger?.LogInformation("Reconciliation.CreateAsync updated existing reconciliation {RecId} for user {UserId} bank {BankId}", existing.Id, userId, dto.BankId);
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

            _logger?.LogInformation("Reconciliation.CreateAsync created reconciliation {RecId} for user {UserId} bank {BankId}", rec.Id, userId, dto.BankId);

            return rec;
        }


        public async Task<ReconciliationSuggestionDto> SuggestAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);

            // Calculate system totals from Incomes and Expenses, filtered by bankId when provided
            // Ignore internal transfers (IsTransfer = true)
            decimal incomeTotal = 0m;
            decimal expenseTotal = 0m;

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                incomeTotal = await _db.Set<Income>()
                    .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end
                                && (!bankId.HasValue || i.BankId == bankId.Value))
                    .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                expenseTotal = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end
                                && (!bankId.HasValue || e.BankId == bankId.Value))
                    .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            }

            // Net system total for the given bank (or for the whole user if bankId null)
            var systemTotal = incomeTotal - expenseTotal;

            // Get closing balance for bank/month
            decimal closingBalance = 0m;
            var reconQuery = _db.Reconciliations
                .Where(r => r.UserId == userId && r.Year == year && r.Month == month);

            if (bankId.HasValue)
                reconQuery = reconQuery.Where(r => r.BankId == bankId.Value);

            var recon = await reconQuery
                .OrderByDescending(r => r.CreatedAt)
                .FirstOrDefaultAsync(ct);

            if (recon != null)
                closingBalance = recon.ClosingBalance;

            var difference = systemTotal - closingBalance;

            // Build transaction list ignoring internal transfers
            var txList = new List<(int Id, decimal Amount, string Description, DateTime Date, string? CategoryName)>();

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                var incomes = await _db.Set<Income>()
                    .Where(i => i.UserId == userId && i.Date >= start && i.Date <= end
                                && (!bankId.HasValue || i.BankId == bankId.Value))
                    .Select(i => new { i.Id, i.Amount, i.Description, i.Date, CategoryName = i.Category != null ? i.Category.Name : "" })
                    .ToListAsync(ct);

                txList.AddRange(incomes.Select(i => (i.Id, i.Amount, i.Description ?? string.Empty, i.Date, i.CategoryName)));
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                var expenses = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId && e.Date >= start && e.Date <= end
                                && (!bankId.HasValue || e.BankId == bankId.Value)                                )
                    .Select(e => new { e.Id, e.Amount, e.Description, e.Date, CategoryName = e.Category != null ? e.Category.Name : "" })
                    .ToListAsync(ct);

                txList.AddRange(expenses.Select(e => (e.Id, -e.Amount, e.Description ?? string.Empty, e.Date, e.CategoryName)));
            }

            // Build suggestions list
            var suggestions = txList
                .OrderByDescending(t => Math.Abs((double)t.Amount))
                .Select(t => new
                {
                    Type = "Tx",
                    TransactionId = t.Id,
                    Amount = t.Amount,
                    Description = t.Description,
                    Date = t.Date,
                    Category = t.CategoryName
                })
                .ToList<object>();

            // Add ExactDiff suggestions if any transaction matches the difference
            foreach (var tx in txList.OrderByDescending(t => Math.Abs((double)t.Amount)))
            {
                if (Math.Abs(tx.Amount - difference) <= 0.01m)
                {
                    suggestions.Insert(0, new
                    {
                        Type = "ExactDiff",
                        TransactionId = tx.Id,
                        Amount = tx.Amount,
                        Description = tx.Description,
                        Date = tx.Date,
                        Category = tx.CategoryName,
                        Reason = "Transaction equals system-closing difference"
                    });
                }
            }

            return new ReconciliationSuggestionDto(systemTotal, closingBalance, difference, suggestions);
        }



        public async Task<bool> MarkReconciledAsync(Guid id, DateTime? reconciledAt = null, CancellationToken ct = default)
        {
            var recon = await _db.Reconciliations.FindAsync(new object[] { id }, ct);
            if (recon == null) return false;

            var year = recon.Year;
            var month = recon.Month;
            var userId = recon.UserId;
            var bankId = recon.BankId;

            // Reuse SuggestAsync to compute systemTotal and closingBalance
            var suggestion = await SuggestAsync(year, month, bankId, ct);
            var difference = suggestion.Difference;

            var tol = 0.01m;
            if (Math.Abs(difference) > tol)
            {
                return false;
            }

            // Mark as reconciled and set the reconciledAt date if provided (otherwise use UTC now)
            recon.Reconciled = true;
            recon.ReconciledAt = reconciledAt ?? DateTime.UtcNow;
            _db.Reconciliations.Update(recon);
            await _db.SaveChangesAsync(ct);

            return true;
        }
    }
}