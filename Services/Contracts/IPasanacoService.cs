using PersonalFinance.Api.Models.Dtos.Pasanaco;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public class RelatedSummaryDto
    {
        public bool HasAnyRelated => PaymentsCount > 0 || LoansCount > 0 || ExpensesCount > 0 || IncomesCount > 0;
        public int PaymentsCount { get; set; }
        public int LoansCount { get; set; }
        public int ExpensesCount { get; set; }
        public int IncomesCount { get; set; }
    }

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

        // --- Nuevos métodos para proteger borrado de Pasanaco ---
        // Devuelve un resumen de elementos relacionados para que el controller pueda informar al usuario
        Task<RelatedSummaryDto> GetRelatedSummaryAsync(string pasanacoId);

        // Borra en cascada de forma controlada (solo con ?force=true y autorización)
        Task DeletePasanacoCascadeAsync(string pasanacoId, Guid performedByUserId);
    }

}