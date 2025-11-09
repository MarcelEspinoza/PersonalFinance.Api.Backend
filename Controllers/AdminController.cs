using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")] // requiere policy existente en Program.cs (AdminOnly)
    public class AdminController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;

        public AdminController(UserManager<ApplicationUser> userManager, RoleManager<IdentityRole<Guid>> roleManager)
        {
            _userManager = userManager;
            _roleManager = roleManager;
        }

        // GET: api/admin/users
        [HttpGet("users")]
        public IActionResult GetUsers()
        {
            var users = _userManager.Users
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    fullName = u.FullName
                })
                .ToList();

            return Ok(users);
        }

        // GET: api/admin/users/{id}/roles
        [HttpGet("users/{id:guid}/roles")]
        public async Task<IActionResult> GetUserRoles(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();
            var roles = await _userManager.GetRolesAsync(user);
            return Ok(new { roles });
        }

        // POST: api/admin/users/{id}/roles
        // Body: { "role": "Admin" }
        [HttpPost("users/{id:guid}/roles")]
        public async Task<IActionResult> AddRoleToUser(Guid id, [FromBody] AddRoleRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Role)) return BadRequest("Role required");

            var roleName = req.Role.Trim();
            // Ensure role exists
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
            }

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            if (await _userManager.IsInRoleAsync(user, roleName))
                return Conflict("User already in role");

            var res = await _userManager.AddToRoleAsync(user, roleName);
            if (!res.Succeeded) return BadRequest(res.Errors);

            return Ok();
        }

        // DELETE: api/admin/users/{id}/roles/{roleName}
        [HttpDelete("users/{id:guid}/roles/{roleName}")]
        public async Task<IActionResult> RemoveRoleFromUser(Guid id, string roleName)
        {
            if (string.IsNullOrWhiteSpace(roleName)) return BadRequest("Role required");

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            if (!await _userManager.IsInRoleAsync(user, roleName))
                return NotFound("User does not have that role");

            var res = await _userManager.RemoveFromRoleAsync(user, roleName);
            if (!res.Succeeded) return BadRequest(res.Errors);

            return NoContent();
        }

        public record AddRoleRequest(string Role);
    }
}
