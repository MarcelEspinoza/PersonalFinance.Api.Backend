using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Services;
using System.Security.Claims;

namespace PersonalFinance.Api.Controllers
{
    // Category controller to manage categories CRUD
    [ApiController]
    [Route("api/[controller]")]
    public class CategoryController : ControllerBase
    {
        private readonly ICategoryService _categoryService;

        public CategoryController(ICategoryService categoryService)
        {
            _categoryService = categoryService;
        }

        private Guid? GetCurrentUserId()
        {
            var userIdClaim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value
                              ?? User.FindFirst("id")?.Value;

            if (string.IsNullOrEmpty(userIdClaim)) return null;
            if (!Guid.TryParse(userIdClaim, out var userId)) return null;
            return userId;
        }

        // GET: api/category
        [HttpGet]
        [Authorize]
        public async Task<IActionResult> GetAll(CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var categories = await _categoryService.GetAllAsync(userId.Value, cancellationToken);
            var result = categories.Select(c => new
            {
                id = c.Id,
                name = c.Name,
                description = c.Description,
                isActive = c.IsActive,
                createdAt = c.CreatedAt,
                updatedAt = c.UpdatedAt
            });

            return Ok(result);
        }

        // GET: api/category/5
        [HttpGet("{id}")]
        [Authorize]
        public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var category = await _categoryService.GetByIdAsync(id, userId.Value, cancellationToken);
            if (category == null) return NotFound();

            return Ok(new
            {
                id = category.Id,
                name = category.Name,
                description = category.Description,
                isActive = category.IsActive,
                createdAt = category.CreatedAt,
                updatedAt = category.UpdatedAt
            });
        }

        // POST: api/category
        [HttpPost]
        [Authorize]
        public async Task<IActionResult> Create([FromBody] CreateCategoryDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var created = await _categoryService.CreateAsync(userId.Value, dto, cancellationToken);
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, new
                {
                    id = created.Id,
                    name = created.Name,
                    description = created.Description,
                    isActive = created.IsActive,
                    createdAt = created.CreatedAt
                });
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // PUT: api/category/5
        [HttpPut("{id}")]
        [Authorize]
        public async Task<IActionResult> Update(int id, [FromBody] UpdateCategoryDto dto, CancellationToken cancellationToken = default)
        {
            if (!ModelState.IsValid) return BadRequest(ModelState);

            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            try
            {
                var updated = await _categoryService.UpdateAsync(id, userId.Value, dto, cancellationToken);
                if (!updated) return NotFound();
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
            catch (InvalidOperationException ex)
            {
                return Conflict(new { error = ex.Message });
            }
        }

        // DELETE: api/category/5
        [HttpDelete("{id}")]
        [Authorize]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            var userId = GetCurrentUserId();
            if (userId == null) return Unauthorized();

            var deleted = await _categoryService.DeleteAsync(id, userId.Value, cancellationToken);
            if (!deleted) return NotFound();

            return NoContent();
        }
    }
}
