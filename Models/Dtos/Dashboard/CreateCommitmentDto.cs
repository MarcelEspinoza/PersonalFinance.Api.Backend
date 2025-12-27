namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    public class CreateCommitmentDto
    {
        public string Name { get; set; } = string.Empty;
        public string FlowType { get; set; } = "Expense";
        public string Nature { get; set; } = "Fixed";
        public decimal ExpectedAmount { get; set; }
        public decimal Tolerance { get; set; }
        public DateTime StartMonth { get; set; }
        public DateTime? EndMonth { get; set; }
        public int? CategoryId { get; set; }
        public Guid? BankId { get; set; }
        public string? Notes { get; set; }
    }

}
