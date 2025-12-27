namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class BudgetDto
    {
        public Guid Id { get; set; }
        public int CategoryId { get; set; }
        public decimal LimitAmount { get; set; }
    }

}
