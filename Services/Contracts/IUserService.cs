using PersonalFinance.Api.Models.Dtos.User;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IUserService
    {
        Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<User?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
        Task<User> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(Guid id, UpdateUserDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
    }
}
