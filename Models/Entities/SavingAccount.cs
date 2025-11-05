using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class SavingAccount
    {
        public int Id { get; set; }
        public Guid UserId { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Balance { get; set; }
        public ICollection<SavingMovement> Movements { get; set; } = new List<SavingMovement>();
    }
}
