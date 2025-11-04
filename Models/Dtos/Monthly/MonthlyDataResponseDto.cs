namespace PersonalFinance.Api.Models.Dtos.Monthly
{
    public class MonthlyDataResponseDto
    {
        public List<MonthlyTransactionDto> Transactions { get; set; } = new();
    }
}
