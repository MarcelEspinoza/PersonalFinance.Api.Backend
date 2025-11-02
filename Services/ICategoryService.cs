using PersonalFinance.Api.Controllers;
using PersonalFinance.Api.Models.Dtos.Category;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services
{
    // Category service interface
    public interface ICategoryService
    {
        Task<IEnumerable<Category>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Category?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default);
        Task<Category> CreateAsync(Guid userId, CreateCategoryDto dto, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(int id, Guid userId, UpdateCategoryDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default);
        Task<Category?> FindByNameAsync(Guid userId, string name, CancellationToken cancellationToken);

    }
}
