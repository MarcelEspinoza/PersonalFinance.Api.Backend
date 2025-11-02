using System.ComponentModel.DataAnnotations.Schema;
using PersonalFinance.Api.Enums;

namespace PersonalFinance.Api.Models.Entities
{
    public class Income
    {
        public int Id { get; set; }
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }
        public string? Description { get; set; }
        public DateTime Date { get; set; }
        public IncomeType Type { get; set; }
        // Foreign key to Category
        public int CategoryId { get; set; }
        // Optional navigation property
        public Category? Category { get; set; }
        public Guid UserId { get; set; }
    }
}
