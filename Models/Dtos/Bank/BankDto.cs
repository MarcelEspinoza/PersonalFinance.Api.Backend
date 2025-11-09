namespace PersonalFinance.Api.Models.Dtos.Bank
{
    public record BankDto(Guid Id, string Name, string? Institution, string? AccountNumber, string Currency);
    public record CreateBankDto(string Name, string? Institution, string? AccountNumber, string? Currency);
}
