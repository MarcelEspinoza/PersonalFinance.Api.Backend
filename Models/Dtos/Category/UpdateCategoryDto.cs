using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Category
{
    public class UpdateCategoryDto
    {
        [StringLength(100)]
        public string? Name { get; set; }

        public string? Description { get; set; }

        public bool? IsActive { get; set; }
    }
}
