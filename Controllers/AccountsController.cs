using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Extensions;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;

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
    }
}
