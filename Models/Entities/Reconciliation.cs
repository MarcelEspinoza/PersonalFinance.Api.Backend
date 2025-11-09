namespace PersonalFinance.Api.Models.Entities
{
    using System.ComponentModel.DataAnnotations;

    public class Reconciliation
    {
        public Guid Id { get; set; }

        [Required]
        public Guid UserId { get; set; }

        [Required]
        public Guid BankId { get; set; }

        [Required]
        public int Year { get; set; }

        [Required]
        public int Month { get; set; } 

        [Required]
        public decimal ClosingBalance { get; set; }

        public string? Notes { get; set; }

        public bool Reconciled { get; set; } = false;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ReconciledAt { get; set; }
    }
}
