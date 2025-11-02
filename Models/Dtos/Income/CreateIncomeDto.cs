using PersonalFinance.Api.Enums;
using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Income
{        // Income DTOs updated to reference Category by id (relation)
    public class CreateIncomeDto
    {
        [Required]
        public decimal Amount { get; set; }

        public string? Description { get; set; }

        [Required]
        public DateTime Date { get; set; }

        // now use CategoryId to reference the Category entity
        [Required]
        public int CategoryId { get; set; }

        [Required]
        public IncomeType Type { get; set; } = IncomeType.Fixed;
    }
}
