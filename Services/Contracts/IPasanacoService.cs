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

    // Todas las operaciones se limitan a los pasanacos del usuario indicado (UserId == userId).
    // Un pasanaco ajeno o inexistente se trata como no encontrado.
    public interface IPasanacoService
    {
        Task<IEnumerable<PasanacoDto>> GetAllAsync(Guid userId);
        Task<PasanacoDto> GetByIdAsync(string id, Guid userId);
        Task<PasanacoDto> CreateAsync(Guid userId, CreatePasanacoDto dto);
        Task UpdateAsync(string id, Guid userId, UpdatePasanacoDto dto);
        Task DeleteAsync(string id, Guid userId);
        Task CompleteAsync(string id, Guid userId);
        Task ReopenAsync(string id, Guid userId);

        Task<IEnumerable<ParticipantDto>> GetParticipantsAsync(string pasanacoId, Guid userId);
        Task AddParticipantAsync(string pasanacoId, Guid userId, CreateParticipantDto dto);
        Task DeleteParticipantAsync(string pasanacoId, string participantId, Guid userId);

        Task<IEnumerable<PasanacoPaymentDto>> GetPaymentsAsync(string pasanacoId, Guid userId, int month, int year);
        Task GeneratePaymentsAsync(string pasanacoId, Guid userId, int month, int year);
        Task MarkPaymentAsPaidAsync(string paymentId, Guid userId, int? transactionId);
        Task<bool> MarkPaymentAsPaidAsync(Guid paymentId, Guid userId);

        Task<bool> RetreatRoundAsync(string pasanacoId, Guid userId);
        Task<bool> AdvanceRoundAsync(string pasanacoId, Guid userId, bool createLoans = false);
        Task<Loan> CreateLoanForParticipantAsync(string pasanacoId, string participantId, decimal amount, Guid userId, string? note = null);
        Task<PasanacoPayment?> GetPaymentByTransactionIdAsync(int transactionId, Guid userId);
        (int month, int year) GetCurrentMonthYearForPasanaco(PasanacoDto pasanaco);

        // Buscar payment por loanId (PaidByLoanId)
        Task<PasanacoPayment?> GetPaymentByLoanIdAsync(Guid loanId, Guid userId);

        // Deshacer pago: borrar transacción/loan si aplica y dejar payment como no pagado.
        Task<bool> UndoPaymentAsync(string paymentId, Guid userId);

        // --- Nuevos métodos para proteger borrado de Pasanaco ---
        // Devuelve un resumen de elementos relacionados para que el controller pueda informar al usuario
        Task<RelatedSummaryDto> GetRelatedSummaryAsync(string pasanacoId, Guid userId);

        // Borra en cascada de forma controlada (solo con ?force=true y autorización)
        Task DeletePasanacoCascadeAsync(string pasanacoId, Guid userId);
    }

}