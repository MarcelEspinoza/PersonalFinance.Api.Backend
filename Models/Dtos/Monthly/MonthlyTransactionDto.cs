namespace PersonalFinance.Api.Models.Dtos.Monthly
{
    public class MonthlyTransactionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "income" o "expense"
        public string Source { get; set; } = string.Empty; // "fixed", "variable", "temporary"
    }

}
