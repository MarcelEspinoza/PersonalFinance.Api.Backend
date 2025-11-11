namespace PersonalFinance.Api.Models.Dtos.Bank
{
    public record BankDto(Guid Id, string Name, string? Entity, string? AccountNumber, string? Currency, string? Color);
    public record CreateBankDto(string Name, string? Entity, string? AccountNumber, string? Currency, string? Color);
}
