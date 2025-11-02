namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class SummaryDto
    {
        public decimal TotalIncome { get; set; }
        public decimal TotalExpense { get; set; }
        public decimal Balance { get; set; }
        public decimal Savings { get; set; }
    }

}
