namespace PersonalFinance.Api.Models.Dtos
{
    public class LoanPaymentDto
    {
        public Guid Id { get; set; }
        public Guid LoanId { get; set; }
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string? Notes { get; set; }
        public int? ExpenseId { get; set; }
    }
}
