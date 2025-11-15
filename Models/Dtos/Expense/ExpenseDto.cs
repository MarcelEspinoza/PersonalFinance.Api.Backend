using System.ComponentModel.DataAnnotations.Schema;

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

        public string? PasanacoId { get; set; }

        [ForeignKey("PasanacoId")]
        public Entities.Pasanaco? Pasanaco { get; set; }

        public Guid? BankId { get; set; }

        [ForeignKey(nameof(BankId))]
        public Entities.Bank? Bank { get; set; }

        public bool IsTransfer { get; set; } = false;
        public string? TransferId { get; set; }                 // UUID/string que liga ambos movimientos
        public Guid? TransferCounterpartyBankId { get; set; }   // BankId del otro lado
        public string? TransferReference { get; set; }
        public string? BankName { get; internal set; }
    }
}
