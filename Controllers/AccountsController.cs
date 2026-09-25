using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using LegacyBank = PersonalFinance.Api.Models.Entities.Bank;

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
                .Select(a => ToDto(a))
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
                OpeningDate = dto.OpeningDate,
                Entity = dto.Entity?.Trim(),
                AccountNumber = dto.AccountNumber?.Trim(),
                Color = dto.Color?.Trim()
            };

            _db.Accounts.Add(account);
            MirrorToBank(account);
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(account));
        }

        [HttpPut("{id:guid}")]
        public async Task<ActionResult<AccountDto>> Update(
            Guid id, [FromBody] UpdateAccountDto dto, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value, ct);
            if (account is null) return NotFound();

            var name = dto.Name.Trim();
            var currency = dto.Currency.Trim().ToUpperInvariant();
            if (name.Length == 0 || currency.Length != 3)
                return BadRequest("Nombre y divisa son obligatorios.");

            account.Name = name;
            account.Type = dto.Type;
            account.Currency = currency;
            account.Entity = dto.Entity?.Trim();
            account.AccountNumber = dto.AccountNumber?.Trim();
            account.Color = dto.Color?.Trim();
            account.IsActive = dto.IsActive;

            MirrorToBank(account);
            await _db.SaveChangesAsync(ct);

            return Ok(ToDto(account));
        }

        [HttpDelete("{id:guid}")]
        public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
        {
            var userId = User.GetUserId();
            if (userId is null) return Unauthorized();

            var account = await _db.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId.Value, ct);
            if (account is null) return NotFound();

            _db.Accounts.Remove(account);

            var bank = await _db.Banks.FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId.Value, ct);
            if (bank is not null) _db.Banks.Remove(bank);

            await _db.SaveChangesAsync(ct);
            return NoContent();
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

        private static AccountDto ToDto(Account a) => new()
        {
            Id = a.Id,
            Name = a.Name,
            Type = a.Type,
            Currency = a.Currency,
            OpeningBalance = a.OpeningBalance,
            OpeningDate = a.OpeningDate,
            IsActive = a.IsActive,
            Entity = a.Entity,
            AccountNumber = a.AccountNumber,
            Color = a.Color
        };

        /// <summary>
        /// El módulo legado (Gastos/Ingresos/Compromisos/Conciliación) todavía lee la
        /// tabla Banks por su cuenta; para que exista una sola lista de cuentas
        /// visible en toda la app, cada alta/edición aquí crea o actualiza también
        /// su fila espejo en Banks, reutilizando el mismo Id.
        /// </summary>
        private void MirrorToBank(Account account)
        {
            var bank = _db.Banks.Local.FirstOrDefault(b => b.Id == account.Id)
                ?? _db.Banks.FirstOrDefault(b => b.Id == account.Id);

            if (bank is null)
            {
                bank = new LegacyBank { Id = account.Id, UserId = account.UserId };
                _db.Banks.Add(bank);
            }

            bank.Name = account.Name;
            bank.Entity = account.Entity;
            bank.AccountNumber = account.AccountNumber;
            bank.Currency = account.Currency;
            bank.Color = account.Color;
        }
    }
}
