namespace PersonalFinance.Api.Models.Dtos.Budgets
{
    public class BudgetStatusDto
    {
        public Guid BudgetId { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;

        public decimal MonthlyLimit { get; set; }
        public decimal SpentAmount { get; set; }
        public decimal RemainingAmount => MonthlyLimit - SpentAmount;

        public bool IsExceeded => SpentAmount > MonthlyLimit;
        public bool IsNearLimit => SpentAmount >= MonthlyLimit * 0.8m && SpentAmount <= MonthlyLimit;
    }
}
