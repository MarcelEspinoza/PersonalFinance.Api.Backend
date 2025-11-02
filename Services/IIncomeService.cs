using PersonalFinance.Api.Models.Dtos.Income;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services
{
    public interface IIncomeService
    {
        Task<IEnumerable<Income>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<Income?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default);
        Task<Income> CreateAsync(Guid userId, CreateIncomeDto dto, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(int id, Guid userId, UpdateIncomeDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default);
    }
}
