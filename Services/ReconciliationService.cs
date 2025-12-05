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

        // =====================================================
        // 📘 Obtener conciliaciones de un mes
        // =====================================================
        public async Task<IEnumerable<Reconciliation>> GetForMonthAsync(int year, int month, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            return await _db.Set<Reconciliation>()
                .Where(r => r.UserId == userId && r.Year == year && r.Month == month)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync(ct);
        }

        // =====================================================
        // 🟢 Crear o actualizar conciliación manual
        // =====================================================
        public async Task<Reconciliation?> CreateAsync(CreateReconciliationDto dto, CancellationToken ct = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var userId = CurrentUserId();

            if (dto.BankId == Guid.Empty)
                throw new ArgumentException("BankId is required and must be a valid GUID.", nameof(dto.BankId));

            var existing = await _db.Set<Reconciliation>()
                .FirstOrDefaultAsync(r => r.UserId == userId && r.BankId == dto.BankId && r.Year == dto.Year && r.Month == dto.Month, ct);

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

        // =====================================================
        // 🔍 Sugerir conciliación automática (toma TODOS los movimientos, incluidas transferencias)
        // =====================================================
        public async Task<ReconciliationSuggestionDto> SuggestAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var start = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
            var end = start.AddMonths(1).AddTicks(-1);

            // 1️⃣ Obtener saldo final del mes anterior (si existe)
            decimal openingBalance = 0m;
            if (bankId.HasValue)
            {
                var prevRecon = await _db.Reconciliations
                    .Where(r => r.UserId == userId && r.BankId == bankId.Value &&
                                (r.Year < year || (r.Year == year && r.Month < month)))
                    .OrderByDescending(r => r.Year)
                    .ThenByDescending(r => r.Month)
                    .FirstOrDefaultAsync(ct);

                if (prevRecon != null)
                    openingBalance = prevRecon.ClosingBalance;
            }

            // 2️⃣ Totales de ingresos y gastos (INCLUYENDO transferencias)
            decimal incomeTotal = 0m;
            decimal expenseTotal = 0m;

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                incomeTotal = await _db.Set<Income>()
                    .Where(i => i.UserId == userId &&
                                i.Date >= start && i.Date <= end &&
                                (!bankId.HasValue || i.BankId == bankId.Value))
                    .SumAsync(i => (decimal?)i.Amount, ct) ?? 0m;
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                expenseTotal = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId &&
                                e.Date >= start && e.Date <= end &&
                                (!bankId.HasValue || e.BankId == bankId.Value))
                    .SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
            }

            // 3️⃣ Calcular saldo teórico del sistema
            var systemClosingBalance = openingBalance + incomeTotal - expenseTotal;

            // 4️⃣ Obtener conciliación manual (si existe)
            decimal closingBalance = 0m;
            var reconQuery = _db.Reconciliations
                .Where(r => r.UserId == userId && r.Year == year && r.Month == month);
            if (bankId.HasValue)
                reconQuery = reconQuery.Where(r => r.BankId == bankId.Value);

            var recon = await reconQuery.OrderByDescending(r => r.CreatedAt).FirstOrDefaultAsync(ct);
            if (recon != null)
                closingBalance = recon.ClosingBalance;

            // 5️⃣ Diferencia entre saldo bancario y sistema
            var difference = closingBalance - systemClosingBalance;

            // 6️⃣ Transacciones detalladas (para mostrar en UI)
            var txList = new List<(int Id, decimal Amount, string Description, DateTime Date, string? CategoryName)>();

            if (_db.Model.FindEntityType(typeof(Income)) != null)
            {
                var incomes = await _db.Set<Income>()
                    .Where(i => i.UserId == userId &&
                                i.Date >= start && i.Date <= end &&
                                (!bankId.HasValue || i.BankId == bankId.Value))
                    .Select(i => new { i.Id, i.Amount, i.Description, i.Date, CategoryName = i.Category != null ? i.Category.Name : "" })
                    .ToListAsync(ct);

                txList.AddRange(incomes.Select(i => (i.Id, i.Amount, i.Description ?? string.Empty, i.Date, i.CategoryName)));
            }

            if (_db.Model.FindEntityType(typeof(Expense)) != null)
            {
                var expenses = await _db.Set<Expense>()
                    .Where(e => e.UserId == userId &&
                                e.Date >= start && e.Date <= end &&
                                (!bankId.HasValue || e.BankId == bankId.Value))
                    .Select(e => new { e.Id, e.Amount, e.Description, e.Date, CategoryName = e.Category != null ? e.Category.Name : "" })
                    .ToListAsync(ct);

                txList.AddRange(expenses.Select(e => (e.Id, -e.Amount, e.Description ?? string.Empty, e.Date, e.CategoryName)));
            }

            // 7️⃣ Armar detalle ordenado por monto
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

            return new ReconciliationSuggestionDto(systemClosingBalance, closingBalance, difference, suggestions);
        }

        // =====================================================
        // ✅ Marcar conciliación como completada
        // =====================================================
        public async Task<bool> MarkReconciledAsync(Guid id, DateTime? reconciledAt = null, CancellationToken ct = default)
        {
            var recon = await _db.Reconciliations.FindAsync(new object[] { id }, ct);
            if (recon == null) return false;

            var suggestion = await SuggestAsync(recon.Year, recon.Month, recon.BankId, ct);
            var difference = suggestion.Difference;

            if (Math.Abs(difference) > 0.01m)
                return false;

            recon.Reconciled = true;
            recon.ReconciledAt = reconciledAt ?? DateTime.UtcNow;
            _db.Reconciliations.Update(recon);
            await _db.SaveChangesAsync(ct);

            return true;
        }
    }
}
