using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Authorize]
    [Route("api/accounts")]
    public sealed class AccountsController : ControllerBase
    {
        private readonly IAppDbContext _db;

        public AccountsController(IAppDbContext db) => _db = db;

        [HttpGet]
        public async Task<ActionResult<IReadOnlyList<AccountDto>>> Get(
            [FromQuery] bool includeInactive, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var accounts = await _db.Accounts
                .AsNoTracking()
                .Where(a => a.UserId == userId.Value && (includeInactive || a.IsActive))
                .OrderBy(a => a.Name)
                .Select(a => new AccountDto
                {
                    Id = a.Id,
                    Name = a.Name,
                    Type = a.Type,
                    Currency = a.Currency,
                    OpeningBalance = a.OpeningBalance,
                    OpeningDate = a.OpeningDate,
                    IsActive = a.IsActive
                })
                .ToListAsync(ct);

            return Ok(accounts);
        }

        [HttpPost]
        public async Task<ActionResult<AccountDto>> Create(
            [FromBody] CreateAccountDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var name = dto.Name.Trim();
            var currency = dto.Currency.Trim().ToUpperInvariant();
            if (name.Length == 0 || currency.Length != 3)
                return BadRequest("Nombre y divisa son obligatorios.");

            var exists = await _db.Accounts.AnyAsync(
                a => a.UserId == userId.Value && a.Name == name, ct);
            if (exists) return Conflict("Ya existe una cuenta con ese nombre.");

            var account = new Account
            {
                UserId = userId.Value,
                Name = name,
                Type = dto.Type,
                Currency = currency,
                OpeningBalance = dto.OpeningBalance,
                OpeningDate = dto.OpeningDate
            };

            _db.Accounts.Add(account);
            await _db.SaveChangesAsync(ct);

            return Ok(new AccountDto
            {
                Id = account.Id,
                Name = account.Name,
                Type = account.Type,
                Currency = account.Currency,
                OpeningBalance = account.OpeningBalance,
                OpeningDate = account.OpeningDate,
                IsActive = account.IsActive
            });
        }

        /// <summary>
        /// Saldo bancario real de la cuenta a fin del mes dado: apertura +
        /// asientos liquidados. Es el número que debe coincidir con el
        /// extracto del banco, independiente del presupuesto combinado.
        /// </summary>
        [HttpGet("{id:guid}/balance")]
        public async Task<ActionResult<AccountBalanceDto>> GetBalance(
            Guid id, [FromQuery] int year, [FromQuery] int month, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();
            if (month is < 1 or > 12) return BadRequest("El mes debe estar entre 1 y 12.");

            var account = await _db.Accounts
                .AsNoTracking()
                .FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value, ct);
            if (account is null) return NotFound();

            var asOf = new DateOnly(year, month, DateTime.DaysInMonth(year, month));
            var entries = await _db.LedgerEntries
                .AsNoTracking()
                .Where(e => e.UserId == userId.Value && e.AccountId == id && e.Status == EntryStatus.Paid)
                .ToListAsync(ct);

            var balance = AccountBalanceCalculator.ComputeBalance(account, entries, asOf);

            return Ok(new AccountBalanceDto
            {
                AccountId = account.Id,
                Year = year,
                Month = month,
                Balance = balance
            });
        }
    }
}
