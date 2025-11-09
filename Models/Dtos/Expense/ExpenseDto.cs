namespace PersonalFinance.Api.Models.Dtos.Expense
{
    public class ExpenseDto
    {
        public int Id { get; set; }
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public DateTime? Start_Date { get; set; }
        public DateTime? End_Date { get; set; }
        public string? Notes { get; set; }

        // 👇 relación con préstamo
        public Guid? LoanId { get; set; }
        public Guid UserId { get; set; }

        public bool IsIndefinite { get; set; }

        public Guid? BankId { get; set; }
    }
}
