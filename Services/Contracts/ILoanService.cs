using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ILoanService
    {
        Task<IEnumerable<Loan>> GetLoansAsync(Guid userId);
        Task<Loan?> GetLoanAsync(Guid id);
        Task<Loan> CreateLoanAsync(Loan loan);
        Task UpdateLoanAsync(Loan loan);
        Task DeleteLoanAsync(Guid id);
    }
}
