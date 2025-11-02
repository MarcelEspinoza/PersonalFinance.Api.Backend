using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models
{
    public class CreateUserDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; init; } = string.Empty;

        [Required]
        public string Password { get; init; } = string.Empty;

        public string? FullName { get; init; }
    }
}