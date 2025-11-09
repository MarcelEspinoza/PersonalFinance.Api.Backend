using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public UsersController(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User?.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return null;
            if (!Guid.TryParse(userIdClaim, out var userId)) return null;
            return userId;
        }

        // GET: api/users
        // Requiere autorización (Admin normalmente) — aquí mantiene [Authorize]
        [HttpGet]
        [Authorize]
        public IActionResult GetAll()
        {
            // UserManager.Users es IQueryable<ApplicationUser>
            var users = _userManager.Users.Select(u => new
            {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email
            }).ToList();

            return Ok(users);
        }

        // GET: api/users/{id}
        [HttpGet("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();
            return Ok(new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email
            });
        }

        // POST: api/users
        // Nota: normalmente el registro se maneja en AuthController. Dejamos Create para compatibilidad.
        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var existing = await _userManager.FindByEmailAsync(dto.Email);
            if (existing != null) return Conflict(new { error = "Email already registered" });

            var user = new ApplicationUser
            {
                Id = Guid.NewGuid(),
                UserName = dto.Email,
                Email = dto.Email,
                EmailConfirmed = dto.EmailConfirmed,
                FullName = dto.FullName ?? string.Empty
            };

            var createRes = await _userManager.CreateAsync(user, dto.Password);
            if (!createRes.Succeeded)
            {
                var errors = createRes.Errors.Select(e => e.Description);
                return BadRequest(new { errors = errors });
            }

            // assign default role if exists
            await _userManager.AddToRoleAsync(user, "Invited");

            return CreatedAtAction(nameof(GetById), new { id = user.Id }, new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email
            });
        }

        // PUT: api/users/{id}
        // Only updates FullName and Email. For password changes use request-password-reset/reset-password flow.
        [HttpPut("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();
            if (currentUserId.Value != id) return Forbid();

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            var changed = false;

            if (!string.IsNullOrWhiteSpace(dto.FullName) && dto.FullName != user.FullName)
            {
                user.FullName = dto.FullName;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
            {
                var setMailRes = await _userManager.SetEmailAsync(user, dto.Email);
                if (!setMailRes.Succeeded)
                {
                    var errors = setMailRes.Errors.Select(e => e.Description);
                    return BadRequest(new { errors });
                }

                // update username to keep them aligned if you used email as username
                var setUserNameRes = await _userManager.SetUserNameAsync(user, dto.Email);
                if (!setUserNameRes.Succeeded)
                {
                    var errors = setUserNameRes.Errors.Select(e => e.Description);
                    return BadRequest(new { errors });
                }

                changed = true;
            }

            if (changed)
            {
                var updateRes = await _userManager.UpdateAsync(user);
                if (!updateRes.Succeeded)
                {
                    var errors = updateRes.Errors.Select(e => e.Description);
                    return BadRequest(new { errors });
                }
            }

            // password updates are not handled here — use password reset endpoints
            if (!string.IsNullOrWhiteSpace(dto.Password))
                return BadRequest(new { error = "Change password via reset-password endpoint" });

            return NoContent();
        }

        // DELETE: api/users/{id}
        [HttpDelete("{id:guid}")]
        [Authorize]
        public async Task<IActionResult> Delete(Guid id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();
            if (currentUserId.Value != id) return Forbid();

            var user = await _userManager.FindByIdAsync(id.ToString());
            if (user == null) return NotFound();

            var res = await _userManager.DeleteAsync(user);
            if (!res.Succeeded)
            {
                var errors = res.Errors.Select(e => e.Description);
                return BadRequest(new { errors });
            }

            return NoContent();
        }

        // GET: api/users/me
        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();
            if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var appUser = await _userManager.FindByIdAsync(userId.ToString());
            if (appUser == null) return NotFound();

            var roles = await _userManager.GetRolesAsync(appUser);

            return Ok(new
            {
                id = appUser.Id,
                fullName = appUser.FullName,
                email = appUser.Email,
                roles = roles // lista de strings
            });
        }
    }
}