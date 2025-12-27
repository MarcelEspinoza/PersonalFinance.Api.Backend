namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class CreateBudgetDto
    {
        public string Name { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public decimal MonthlyLimit { get; set; }
        public DateTime StartMonth { get; set; }
        public DateTime? EndMonth { get; set; }
    }

}
