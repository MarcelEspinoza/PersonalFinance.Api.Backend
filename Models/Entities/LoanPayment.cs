using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class LoanPayment
    {
        public Guid Id { get; set; }
        public Guid LoanId { get; set; }
        public Loan? Loan { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public DateTime PaymentDate { get; set; }
        public string? Notes { get; set; }

        // Opcional: referencia al Expense que lo originó (recomendado)
        public int? ExpenseId { get; set; }
        public Expense? Expense { get; set; }

        public int? IncomeId { get; set; }
        public Income? Income { get; set; }
    }
}
