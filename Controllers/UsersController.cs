using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserService _userService;

        public UsersController(IUserService userService)
        {
            _userService = userService;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return null;
            if (!Guid.TryParse(userIdClaim, out var userId)) return null;
            return userId;
        }

        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll()
        {
            var users = await _userService.GetAllAsync();
            var result = users.Select(u => new
            {
                id = u.Id,
                fullName = u.FullName,
                email = u.Email
            });
            return Ok(result);
        }

        [HttpGet("{id:int}")]
        [Authorize]
        public async Task<IActionResult> GetById(Guid id)
        {
            var user = await _userService.GetByIdAsync(id);
            if (user == null) return NotFound();
            return Ok(new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email
            });
        }

        [HttpPost]
        [AllowAnonymous]
        public async Task<IActionResult> Create([FromBody] CreateUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            try
            {
                var user = await _userService.CreateAsync(dto);
                return CreatedAtAction(nameof(GetById), new { id = user.Id }, new
                {
                    id = user.Id,
                    fullName = user.FullName,
                    email = user.Email
                });
            }
            catch (InvalidOperationException ex) // e.g. email already registered
            {
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPut("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserDto dto)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();
            if (currentUserId.Value != id) return Forbid();

            try
            {
                var updated = await _userService.UpdateAsync(id, dto);
                if (!updated) return NotFound();
                return NoContent();
            }
            catch (InvalidOperationException ex) // e.g. email taken by another user
            {
                return Conflict(new { error = ex.Message });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpDelete("{id:int}")]
        [Authorize]
        public async Task<IActionResult> Delete(Guid id)
        {
            var currentUserId = GetCurrentUserId();
            if (currentUserId == null) return Unauthorized();
            if (currentUserId.Value != id) return Forbid();

            var deleted = await _userService.DeleteAsync(id);
            if (!deleted) return NotFound();
            return NoContent();
        }

        [HttpGet("me")]
        [Authorize]
        public async Task<IActionResult> Me()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return Unauthorized();
            if (!Guid.TryParse(userIdClaim, out var userId)) return Unauthorized();

            var user = await _userService.GetByIdAsync(userId);
            if (user == null) return NotFound();

            return Ok(new
            {
                id = user.Id,
                fullName = user.FullName,
                email = user.Email
            });
        }
    }
}