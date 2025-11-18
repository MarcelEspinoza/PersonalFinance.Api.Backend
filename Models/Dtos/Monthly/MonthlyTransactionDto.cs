using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Dtos.Monthly
{
    public class MonthlyTransactionDto
    {
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime Date { get; set; }
        public int CategoryId { get; set; }
        public string CategoryName { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty; // "income" o "expense"
        public string Source { get; set; } = string.Empty; // "fixed", "variable", "temporary"

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
