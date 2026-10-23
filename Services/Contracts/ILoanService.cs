using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface ILoanService
    {
        Task<IEnumerable<Loan>> GetLoansAsync(Guid userId);
        Task<Loan?> GetLoanAsync(Guid id, Guid userId);
        // dto.UserId es el propietario y debe venir ya fijado por el llamador (token o propietario verificado).
        Task<Loan> CreateLoanAsync(LoanDto dto);
        Task UpdateLoanAsync(Guid id, Guid userId, LoanDto dto);
        Task DeleteLoanAsync(Guid id, Guid userId);
    }
}
