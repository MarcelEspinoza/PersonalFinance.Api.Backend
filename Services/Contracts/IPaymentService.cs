using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IPaymentService
    {
        // Sin comprobación de propietario: el llamador debe haber validado que el préstamo es del usuario.
        Task<LoanPayment> CreatePaymentAsync(Guid loanId, LoanPayment payment);
        // Devuelven null si el préstamo no existe o no pertenece al usuario.
        Task<LoanPayment?> CreatePaymentAsync(Guid userId, Guid loanId, LoanPayment payment);
        Task<IEnumerable<LoanPayment>?> GetPaymentsAsync(Guid userId, Guid loanId);
    }
}
