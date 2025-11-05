using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IPaymentService
    {
        Task<LoanPayment> CreatePaymentAsync(Guid loanId, LoanPayment payment);
        Task<IEnumerable<LoanPayment>> GetPaymentsAsync(Guid loanId);
    }
}
