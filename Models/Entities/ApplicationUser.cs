using Microsoft.AspNetCore.Identity;

namespace PersonalFinance.Api.Models.Entities
{
    // Identity user with Guid as key
    public class ApplicationUser : IdentityUser<Guid>
    {
        // Additional application-specific fields
        public string? FullName { get; set; }

        // Note: IdentityUser already has EmailConfirmed boolean property.
        // Role membership is handled by IdentityRole and UserManager.
    }
}
