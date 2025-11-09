using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;
using System.Security.Claims;

namespace PersonalFinance.Api.Services
{
    public class BankService : IBankService
    {
        private readonly AppDbContext _db;
        private readonly IHttpContextAccessor _http;

        public BankService(AppDbContext db, IHttpContextAccessor http)
        {
            _db = db;
            _http = http;
        }

        private Guid CurrentUserId()
        {
            var uid = _http.HttpContext?.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(uid, out var g) ? g : Guid.Empty;
        }

        public async Task<IEnumerable<Bank>> GetAllAsync(CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            return await _db.Set<Bank>().Where(b => b.UserId == userId).ToListAsync(ct);
        }

        public async Task<Bank?> GetByIdAsync(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var bank = await _db.Set<Bank>().FindAsync(new object[] { id }, ct);
            if (bank == null || bank.UserId != userId) return null;
            return bank;
        }

        public async Task<Bank> CreateAsync(CreateBankDto dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var bank = new Bank
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Name = dto.Name,
                Institution = dto.Institution,
                AccountNumber = dto.AccountNumber,
                Currency = dto.Currency ?? "EUR",
                CreatedAt = DateTime.UtcNow
            };
            _db.Add(bank);
            await _db.SaveChangesAsync(ct);
            return bank;
        }

        public async Task<bool> UpdateAsync(Guid id, CreateBankDto dto, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var bank = await _db.Set<Bank>().FindAsync(new object[] { id }, ct);
            if (bank == null || bank.UserId != userId) return false;
            bank.Name = dto.Name;
            bank.Institution = dto.Institution;
            bank.AccountNumber = dto.AccountNumber;
            bank.Currency = dto.Currency ?? bank.Currency;
            await _db.SaveChangesAsync(ct);
            return true;
        }

        public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
        {
            var userId = CurrentUserId();
            var bank = await _db.Set<Bank>().FindAsync(new object[] { id }, ct);
            if (bank == null || bank.UserId != userId) return false;
            _db.Remove(bank);
            await _db.SaveChangesAsync(ct);
            return true;
        }
    }
}

