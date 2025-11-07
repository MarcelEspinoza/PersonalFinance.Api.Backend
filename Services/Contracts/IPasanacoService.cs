using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IPasanacoService
    {
        Task<IEnumerable<PasanacoDto>> GetAllAsync();
        Task<PasanacoDto> GetByIdAsync(string id);
        Task<PasanacoDto> CreateAsync(CreatePasanacoDto dto);
        Task UpdateAsync(string id, UpdatePasanacoDto dto);
        Task DeleteAsync(string id);

        Task<IEnumerable<ParticipantDto>> GetParticipantsAsync(string pasanacoId);
        Task AddParticipantAsync(string pasanacoId, CreateParticipantDto dto);
        Task DeleteParticipantAsync(string pasanacoId, string participantId);

        Task<IEnumerable<PasanacoPaymentDto>> GetPaymentsAsync(string pasanacoId, int month, int year);
        Task GeneratePaymentsAsync(string pasanacoId, int month, int year);
        Task MarkPaymentAsPaidAsync(string paymentId, int? transactionId);
        Task<bool> MarkPaymentAsPaidAsync(Guid paymentId, Guid userId);

        Task<bool> RetreatRoundAsync(string pasanacoId);
        Task<bool> AdvanceRoundAsync(string pasanacoId, Guid userId, bool createLoans = false);
        Task<Loan> CreateLoanForParticipantAsync(string pasanacoId, string participantId, decimal amount, Guid userId, string? note = null);
        Task<PasanacoPayment?> GetPaymentByTransactionIdAsync(int transactionId);
        (int month, int year) GetCurrentMonthYearForPasanaco(PasanacoDto pasanaco);

        // Buscar payment por loanId (PaidByLoanId)
        Task<PasanacoPayment?> GetPaymentByLoanIdAsync(Guid loanId);

        // Deshacer pago: borrar transacción/loan si aplica y dejar payment como no pagado.
        Task<bool> UndoPaymentAsync(string paymentId, Guid performedByUserId);
    }

}
