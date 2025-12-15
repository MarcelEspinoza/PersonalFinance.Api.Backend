namespace PersonalFinance.Api.Models.Dtos.Monthly
{
    public class MonthlyInsightsDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public string Currency { get; set; } = "EUR";

        public decimal TotalIncomes { get; set; }
        public decimal TotalExpenses { get; set; }
        public decimal Balance => TotalIncomes - TotalExpenses;
        public decimal SavingsRate => TotalIncomes > 0 ? Balance / TotalIncomes : 0;

        public int TxCount { get; set; }
        public int DaysWithSpend { get; set; }
        public decimal AvgDailySpend { get; set; }

        public List<CategorySummary> ByCategory { get; set; } = new();
        public List<TransactionSummary> TopExpenses { get; set; } = new();
        public List<TransactionSummary> TopIncomes { get; set; } = new();
        public TransactionSummary? LargestIncome { get; set; }

        public class CategorySummary
        {
            public int CategoryId { get; set; }
            public string CategoryName { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public decimal Pct { get; set; }
        }

        public class TransactionSummary
        {
            public int Id { get; set; }
            public string Description { get; set; } = string.Empty;
            public decimal Amount { get; set; }
            public DateTime Date { get; set; }
            public string? CategoryName { get; set; }
        }
    }
}
