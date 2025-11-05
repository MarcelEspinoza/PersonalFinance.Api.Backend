using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Services.Contracts;

namespace PersonalFinance.Api.Services
{
    public class CategoryService : ICategoryService
    {
        private readonly AppDbContext _context;

        public CategoryService(AppDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Category>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .AsNoTracking()
                .Where(c => c.UserId == userId || c.IsSystem)
                .ToListAsync(cancellationToken);
        }

        public async Task<Category?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            return await _context.Categories
                .AsNoTracking()
                .Where(c => c.UserId == userId)
                .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);
        }

        public async Task<Category> CreateAsync(Guid userId, CreateCategoryDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            if (string.IsNullOrWhiteSpace(dto.Name)) throw new ArgumentException("Name is required", nameof(dto.Name));
            if (dto.Name.Length > 100) throw new ArgumentException("Name cannot exceed 100 characters", nameof(dto.Name));

            var now = DateTime.UtcNow;
            var category = new Category
            {
                Name = dto.Name.Trim(),
                Description = dto.Description,
                IsActive = dto.IsActive,
                UserId = userId,
                CreatedAt = now
            };

            _context.Categories.Add(category);
            await _context.SaveChangesAsync(cancellationToken);
            return category;
        }

        public async Task<bool> UpdateAsync(int id, Guid userId, UpdateCategoryDto dto, CancellationToken cancellationToken = default)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);

            if (category == null) return false;

            if (dto.Name != null)
            {
                var name = dto.Name.Trim();
                if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be empty", nameof(dto.Name));
                if (name.Length > 100) throw new ArgumentException("Name cannot exceed 100 characters", nameof(dto.Name));
                category.Name = name;
            }

            if (dto.Description != null)
                category.Description = dto.Description;

            if (dto.IsActive.HasValue)
                category.IsActive = dto.IsActive.Value;

            category.UpdatedAt = DateTime.UtcNow;

            _context.Categories.Update(category);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default)
        {
            var category = await _context.Categories
                .FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);

            if (category == null) return false;

            // Soft-delete: mark as inactive and update timestamp.
            category.IsActive = false;
            category.UpdatedAt = DateTime.UtcNow;

            _context.Categories.Update(category);
            await _context.SaveChangesAsync(cancellationToken);
            return true;
        }

        public async Task<Category?> FindByNameAsync(Guid userId, string name, CancellationToken cancellationToken)
        {
            return await _context.Categories
                .FirstOrDefaultAsync(c => c.UserId == userId && c.Name == name, cancellationToken);
        }

    }
}
