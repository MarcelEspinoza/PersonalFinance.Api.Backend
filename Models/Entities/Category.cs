using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Entities
{
    public class Category
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty;

        public string? Description { get; set; }

        // Owner of the category - useful for multi-user apps
        public Guid UserId { get; set; }

        // Soft-delete / enable flag
        public bool IsActive { get; set; } = true;

        // Audit fields
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        // Navigation property (optional)
        public ICollection<Income> Incomes { get; set; } = new List<Income>();
        public ICollection<Expense> Expenses { get; set; } = new List<Expense>(); 
        public bool IsSystem { get; set; } = false;
    }
}
