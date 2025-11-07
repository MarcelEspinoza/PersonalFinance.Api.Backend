using System.ComponentModel.DataAnnotations;

namespace PersonalFinance.Api.Models.Dtos.Auth
{
    public class RequestPasswordResetDto
    {
        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;
    }
}
