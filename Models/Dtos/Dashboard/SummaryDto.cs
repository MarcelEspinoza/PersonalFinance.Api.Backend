namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class SummaryDto
    {
        public decimal CurrentBalance { get; set; }
        public decimal MonthOpeningBalance { get; set; }
        public decimal CurrentMonthIncome { get; set; }
        public decimal CurrentMonthExpense { get; set; }
        public decimal CurrentMonthResult { get; set; }
        public decimal ProjectedBalance { get; set; }
        public decimal ProjectionChange { get; set; }
    }
}
