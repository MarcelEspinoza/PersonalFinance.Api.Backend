using PersonalFinance.Api.Models.Enums;
using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class Loan
    {
        public Guid Id { get; set; }
        public Guid UserId { get; set; }
        public LoanType Type { get; set; }
        public string Name { get; set; } = string.Empty;

        [Column(TypeName = "decimal(18,2)")]
        public decimal PrincipalAmount { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal OutstandingAmount { get; set; }

        public DateTimeOffset StartDate { get; set; }
        public DateTimeOffset? DueDate { get; set; }

        public string Status { get; set; } = "active";

        // Solo para bancarios
        [Column(TypeName = "decimal(5,2)")]
        public decimal? InterestRate { get; set; }
        [Column(TypeName = "decimal(5,2)")]
        public decimal? TAE { get; set; }
        public int? InstallmentsPaid { get; set; }
        public int? InstallmentsRemaining { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal? NextPaymentAmount { get; set; }
        public DateTimeOffset? NextPaymentDate { get; set; }

        // 👇 Nueva relación
        public int CategoryId { get; set; }
        public Category? Category { get; set; }

        public ICollection<LoanPayment> Payments { get; set; } = new List<LoanPayment>();

        public string? PasanacoId { get; set; }

        [ForeignKey("PasanacoId")]
        public Pasanaco? Pasanaco { get; set; }
    }

}
