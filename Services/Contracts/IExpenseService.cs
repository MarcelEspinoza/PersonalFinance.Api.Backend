using PersonalFinance.Api.Models.Dtos.Expense;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IExpenseService
    {
        Task<IEnumerable<ExpenseDto>> GetAllAsync(Guid userId, CancellationToken cancellationToken = default);
        Task<ExpenseDto?> GetByIdAsync(int id, Guid userId, CancellationToken cancellationToken = default);
        Task<Expense> CreateAsync(Guid userId, CreateExpenseDto dto, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(int id, Guid userId, UpdateExpenseDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, Guid userId, CancellationToken cancellationToken = default);
    }
}
