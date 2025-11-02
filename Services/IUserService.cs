using PersonalFinance.Api.Models;

namespace PersonalFinance.Api.Services
{
    public interface IUserService
    {
        Task<IEnumerable<User>> GetAllAsync(CancellationToken cancellationToken = default);
        Task<User?> GetByIdAsync(int id, CancellationToken cancellationToken = default);
        Task<User> CreateAsync(CreateUserDto dto, CancellationToken cancellationToken = default);
        Task<bool> UpdateAsync(int id, UpdateUserDto dto, CancellationToken cancellationToken = default);
        Task<bool> DeleteAsync(int id, CancellationToken cancellationToken = default);
    }
}
