using PersonalFinance.Api.Models.Dtos.Pasanaco;

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
    }

}
