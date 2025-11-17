using PersonalFinance.Api.Models.Dtos.Bank;
using PersonalFinance.Api.Models.Entities;

namespace PersonalFinance.Api.Services.Contracts
{
    public interface IReconciliationService
    {
        Task<IEnumerable<Reconciliation>> GetForMonthAsync(int year, int month, CancellationToken ct = default);
        Task<Reconciliation?> CreateAsync(CreateReconciliationDto dto, CancellationToken ct = default);
        Task<ReconciliationSuggestionDto> SuggestAsync(int year, int month, Guid? bankId = null, CancellationToken ct = default);
        Task<bool> MarkReconciledAsync(Guid id, DateTime? reconciledAt,, CancellationToken ct = default);
    }
}

