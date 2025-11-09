using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IBankService
    {
        Task<IEnumerable<Bank>> GetAllAsync(CancellationToken ct = default);
        Task<Bank?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Bank> CreateAsync(CreateBankDto dto, CancellationToken ct = default);
        Task<bool> UpdateAsync(Guid id, CreateBankDto dto, CancellationToken ct = default);
        Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);
    }
}

