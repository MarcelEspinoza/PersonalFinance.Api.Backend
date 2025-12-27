using System.ComponentModel.DataAnnotations.Schema;

namespace PersonalFinance.Api.Models.Entities
{
    public class Budget
    {
        public Guid Id { get; set; }

        public Guid UserId { get; set; }

        public int CategoryId { get; set; }

        [Column(TypeName = "decimal(18,2)")]
        public decimal MonthlyLimit { get; set; }

        public DateTime StartMonth { get; set; }
        public DateTime? EndMonth { get; set; }

        public bool IsActive { get; set; } = true;

        public Category Category { get; set; } = null!;
    }
}
