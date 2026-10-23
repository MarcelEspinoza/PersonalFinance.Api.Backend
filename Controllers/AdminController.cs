using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")]
    public class AdminController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RoleManager<IdentityRole<Guid>> _roleManager;
        private readonly IEmailSender _emailSender;
        private readonly ILogger<AdminController> _logger;

        public AdminController(
            UserManager<ApplicationUser> userManager,
            RoleManager<IdentityRole<Guid>> roleManager,
            IEmailSender emailSender,
            ILogger<AdminController> logger)
        {
            _userManager = userManager;
            _roleManager = roleManager;
            _emailSender = emailSender;
            _logger = logger;
        }

        // GET: api/admin/users
        // Devuelve lista de usuarios con sus roles (para evitar llamadas adicionales desde la UI)
        [HttpGet("users")]
        public async Task<IActionResult> GetUsers()
        {
            var users = await _userManager.Users
                .Select(u => new
                {
                    id = u.Id,
                    email = u.Email,
                    fullName = u.FullName,
                    emailConfirmed = u.EmailConfirmed
                })
                .ToListAsync();

            var result = new List<object>();
            foreach (var u in users)
            {
                var userEntity = await _userManager.FindByIdAsync(u.id.ToString());
                var roles = userEntity != null ? await _userManager.GetRolesAsync(userEntity) : new List<string>();
                result.Add(new
                {
                    id = u.id,
                    email = u.email,
                    fullName = u.fullName,
                    roles = roles.ToArray(),
                    emailConfirmed = u.emailConfirmed,
                    pendingApproval = IsPendingApproval(roles)
                });
            }

            return Ok(result);
        }

        // GET: api/admin/users/{id}
        [HttpGet("users/{id:guid}")]
        public async Task<IActionResult> GetUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(user);

            return Ok(new
            {
                id = user.Id,
                userName = user.UserName,
                email = user.Email,
                fullName = user.FullName,
                roles = roles.ToArray(),
                emailConfirmed = user.EmailConfirmed,
                pendingApproval = IsPendingApproval(roles)
            });
        }

        // POST: api/admin/users/{id}/approve
        // Aprueba una cuenta pendiente: añade "User", quita "Invited" y devuelve el array actualizado de roles.
        [HttpPost("users/{id:guid}/approve")]
        public async Task<IActionResult> ApproveUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            var wasPending = IsPendingApproval(await _userManager.GetRolesAsync(user));

            if (!await _userManager.IsInRoleAsync(user, "User"))
            {
                var addRes = await _userManager.AddToRoleAsync(user, "User");
                if (!addRes.Succeeded) return BadRequest(addRes.Errors);
            }

            if (await _userManager.IsInRoleAsync(user, "Invited"))
            {
                var removeRes = await _userManager.RemoveFromRoleAsync(user, "Invited");
                if (!removeRes.Succeeded) return BadRequest(removeRes.Errors);
            }

            var roles = await _userManager.GetRolesAsync(user);

            if (wasPending && !string.IsNullOrWhiteSpace(user.Email))
            {
                try
                {
                    await _emailSender.SendEmailAsync(
                        user.Email,
                        "Tu cuenta en Personal Finance ha sido aprobada",
                        EmailTemplates.AccountApprovedTemplate(user.FullName));
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not send approval email to user {UserId}", user.Id);
                }
            }

            return Ok(roles);
        }

        // GET: api/admin/users/{id}/roles
        [HttpGet("users/{id:guid}/roles")]
        public async Task<IActionResult> GetUserRoles(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();
            var roles = await _userManager.GetRolesAsync(user);
            return Ok(roles);
        }

        // POST: api/admin/users/{id}/roles
        // Body: { "role": "Admin" }
        // Ahora devuelve el array actualizado de roles al cliente
        [HttpPost("users/{id:guid}/roles")]
        public async Task<IActionResult> AddRoleToUser(Guid id, [FromBody] AddRoleRequest req)
        {
            if (string.IsNullOrWhiteSpace(req?.Role)) return BadRequest("Role required");

            var roleName = req.Role.Trim();

            // Ensure role exists
            if (!await _roleManager.RoleExistsAsync(roleName))
            {
                var createResult = await _roleManager.CreateAsync(new IdentityRole<Guid>(roleName));
                if (!createResult.Succeeded) return BadRequest(createResult.Errors);
            }

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            if (await _userManager.IsInRoleAsync(user, roleName))
                return Conflict("User already in role");

            var res = await _userManager.AddToRoleAsync(user, roleName);
            if (!res.Succeeded) return BadRequest(res.Errors);

            var roles = await _userManager.GetRolesAsync(user);
            return Ok(roles); // devuelve array actualizado
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

            // devolver roles actualizados (opcional)
            var roles = await _userManager.GetRolesAsync(user);
            return Ok(roles);
        }

        // GET: api/admin/roles
        [HttpGet("roles")]
        public IActionResult GetAllRoles()
        {
            var roles = _roleManager.Roles.Select(r => r.Name).ToList();
            return Ok(roles);
        }

        // DELETE: api/admin/users/{id}
        [HttpDelete("users/{id:guid}")]
        public async Task<IActionResult> DeleteUser(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            var res = await _userManager.DeleteAsync(user);
            if (!res.Succeeded) return BadRequest(res.Errors);

            return NoContent();
        }

        private static bool IsPendingApproval(IEnumerable<string> roles) =>
            !roles.Contains("Admin") && !roles.Contains("User");

        public record AddRoleRequest(string Role);
    }
}