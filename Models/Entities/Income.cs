using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class Income
    {
        public int Id { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public string Type { get; set; } = string.Empty;
        public int CategoryId { get; set; }   
        public Guid UserId { get; set; }
        public DateTime? Start_Date { get; set; }
        public DateTime? End_Date { get; set; }
        public string? Notes { get; set; }
        public Category Category { get; set; } = null!;

        public Guid? LoanId { get; set; }  
        public Loan? Loan { get; set; }

        public bool IsIndefinite { get; set; } = false;
        public string? PasanacoId { get; set; }

        [ForeignKey("PasanacoId")]
        public Pasanaco? Pasanaco { get; set; }

        public Guid? BankId { get; set; }

        [ForeignKey(nameof(BankId))]
        public Bank? Bank { get; set; }

        public bool IsTransfer { get; set; } = false;
        public string? TransferId { get; set; }                 // UUID/string que liga ambos movimientos
        public Guid? TransferCounterpartyBankId { get; set; }   // BankId del otro lado
        public string? TransferReference { get; set; }
    }
}
