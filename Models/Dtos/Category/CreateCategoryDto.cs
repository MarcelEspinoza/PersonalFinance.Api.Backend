using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Category
{
    // Category DTOs
    public class CreateCategoryDto
    {
        [Required]
        [StringLength(100)]
        public string Name { get; set; } = null!;

        public string? Description { get; set; }

        // Defaults to true for usability
        public bool IsActive { get; set; } = true;
    }
}
