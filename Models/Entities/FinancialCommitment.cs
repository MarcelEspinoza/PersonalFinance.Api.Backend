using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class FinancialCommitment
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        // "Income" o "Expense"
        public string Type { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal ExpectedAmount { get; set; }

        // Margen aceptable (ej: nómina variable)
        [Column(TypeName = "decimal(18,2)")]
        public decimal Tolerance { get; set; } = 0m;

        // Mes desde el que aplica
        public DateTime StartMonth { get; set; }

        // Null = indefinido
        public DateTime? EndMonth { get; set; }

        // Opcional
        public int? CategoryId { get; set; }
        public Guid? BankId { get; set; }

        public bool IsActive { get; set; } = true;
    }
}
