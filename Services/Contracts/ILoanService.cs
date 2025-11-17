using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ILoanService
    {
        Task<IEnumerable<Loan>> GetLoansAsync(Guid userId);
        Task<Loan?> GetLoanAsync(Guid id);
        Task<Loan> CreateLoanAsync(LoanDto dto);
        Task UpdateLoanAsync(Guid id, LoanDto dto);
        Task DeleteLoanAsync(Guid id);
    }
}
