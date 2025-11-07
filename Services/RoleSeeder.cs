using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services
{
    public interface IRoleSeeder
    {
        Task EnsureRolesAndAdminAsync();
    }

    public class RoleSeeder : IRoleSeeder
    {
        private readonly IServiceProvider _provider;
        private readonly AppDbContext _db;
        private readonly Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> _userManager;
        private readonly Microsoft.AspNetCore.Identity.RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly IConfiguration _config;
        private readonly ILogger<RoleSeeder> _logger;

        public RoleSeeder(IServiceProvider provider,
                          AppDbContext db,
                          Microsoft.AspNetCore.Identity.UserManager<ApplicationUser> userManager,
                          Microsoft.AspNetCore.Identity.RoleManager<IdentityRole<Guid>> roleManager,
                          IConfiguration config,
                          ILogger<RoleSeeder> logger)
        {
            _provider = provider;
            _db = db;
            _userManager = userManager;
            _roleManager = roleManager;
            _config = config;
            _logger = logger;
        }

        public async Task EnsureRolesAndAdminAsync()
        {
            // Create roles if they don't exist
            var roles = new[] { "Admin", "User", "Invited" };
            foreach (var r in roles)
            {
                if (!await _roleManager.RoleExistsAsync(r))
                {
                    var role = new IdentityRole<Guid>(r);
                    var rc = await _roleManager.CreateAsync(role);
                    if (!rc.Succeeded)
                    {
                        _logger.LogError("Failed creating role {Role}: {Errors}", r, rc.Errors);
                    }
                }
            }

            // If there are no users, optionally create an admin from config
            var anyUser = await _db.Users.AnyAsync();
            if (!anyUser)
            {
                var adminEmail = _config["Seed:AdminEmail"] ?? "admin@example.com";
                var adminPassword = _config["Seed:AdminPassword"] ?? "ChangeMe123!";

                var admin = new ApplicationUser
                {
                    Id = Guid.NewGuid(),
                    UserName = adminEmail,
                    NormalizedUserName = adminEmail.ToUpperInvariant(),
                    Email = adminEmail,
                    NormalizedEmail = adminEmail.ToUpperInvariant(),
                    EmailConfirmed = true,
                    FullName = "Administrator"
                };

                var result = await _userManager.CreateAsync(admin, adminPassword);
                if (result.Succeeded)
                {
                    var addRoleRes = await _userManager.AddToRoleAsync(admin, "Admin");
                    if (!addRoleRes.Succeeded)
                    {
                        _logger.LogError("Failed to assign Admin role to seeded admin: {Errors}", addRoleRes.Errors);
                    }
                    _logger.LogInformation("Seeded initial admin user: {Email}", adminEmail);
                }
                else
                {
                    _logger.LogError("Failed to create seeded admin user: {Errors}", result.Errors);
                }
            }
        }
    }
}