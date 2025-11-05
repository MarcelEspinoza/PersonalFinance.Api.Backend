using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class SavingMovement
    {
        public int Id { get; set; }
        public int SavingAccountId { get; set; }
        public SavingAccount SavingAccount { get; set; } = null!;
        public DateTime Date { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public string? Notes { get; set; }
    }
}
