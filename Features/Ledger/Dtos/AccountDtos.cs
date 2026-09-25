using System.ComponentModel.DataAnnotations;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    public sealed class AccountDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public AccountType Type { get; set; }
        public string Currency { get; set; } = string.Empty;
        public decimal OpeningBalance { get; set; }
        public DateOnly OpeningDate { get; set; }
        public bool IsActive { get; set; }
        public string? Entity { get; set; }
        public string? AccountNumber { get; set; }
        public string? Color { get; set; }
    }

    public sealed class CreateAccountDto
    {
        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; } = AccountType.Checking;

        [Required]
        [StringLength(3, MinimumLength = 3)]
        public string Currency { get; set; } = "EUR";

        public decimal OpeningBalance { get; set; }
        public DateOnly OpeningDate { get; set; }
        public string? Entity { get; set; }
        public string? AccountNumber { get; set; }
        public string? Color { get; set; }
    }

    public sealed class UpdateAccountDto
    {
        [Required]
        [MaxLength(120)]
        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; } = AccountType.Checking;

        [Required]
        [StringLength(3, MinimumLength = 3)]
        public string Currency { get; set; } = "EUR";

        public string? Entity { get; set; }
        public string? AccountNumber { get; set; }
        public string? Color { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public sealed class AccountBalanceDto
    {
        public Guid AccountId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public decimal Balance { get; set; }
    }
}
