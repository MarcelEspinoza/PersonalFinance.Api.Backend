using System.ComponentModel.DataAnnotations;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Zeus
{
    public sealed class BuyZeusPassDto
    {
        public Guid Id { get; set; }

        [Range(1, 6_000_000)]
        public int Minutes { get; set; }

        public Guid? LedgerEntryId { get; set; }
        public ZeusPurchaseExpenseDto? NewExpense { get; set; }
    }

    public sealed class ZeusPurchaseExpenseDto
    {
        public Guid ConceptId { get; set; }
        public Guid AccountId { get; set; }
        public DateOnly Date { get; set; }

        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }
    }

    public sealed class SaveZeusUsageDto
    {
        public Guid Id { get; set; }
        public DateOnly Date { get; set; }

        [Range(1, 1440)]
        public int? Minutes { get; set; }

        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }
    }

    public sealed record ZeusExpenseDto(
        Guid Id, DateOnly Date, string Description, string ConceptName,
        string AccountName, decimal Amount, decimal AllocatedAmount = 0m, EntryStatus Status = EntryStatus.Paid);

    public sealed class SaveZeusCareStayDto
    {
        public Guid Id { get; set; }
        public string Provider { get; set; } = "";
        public string Kind { get; set; } = "";
        public DateOnly Date { get; set; }
        public TimeOnly? StartTime { get; set; }
        public TimeOnly? EndTime { get; set; }
        public int Nights { get; set; }

        [Range(typeof(decimal), "0.01", "999999999")]
        public decimal Amount { get; set; }

        [MaxLength(300)]
        public string? Note { get; set; }
    }

    public sealed class PayZeusCareDto
    {
        public List<Guid> StayIds { get; set; } = [];
        public Guid? LedgerEntryId { get; set; }
        public ZeusPurchaseExpenseDto? NewExpense { get; set; }
        public ZeusPurchaseExpenseDto? NewForecast { get; set; }
    }

    public sealed record ZeusCareStayDto(
        Guid Id, string Provider, string Kind, DateOnly Date,
        TimeOnly? StartTime, TimeOnly? EndTime, int Nights, decimal Amount,
        string? Note, ZeusExpenseDto? Expense);

    public sealed record ZeusPassDto(
        Guid Id, DateOnly PurchasedOn, int Minutes, int UsedMinutes, int RemainingMinutes,
        ZeusExpenseDto Expense);

    public sealed record ZeusUsageAllocationDto(Guid PassId, DateOnly PurchasedOn, int Minutes);

    public sealed record ZeusUsageDto(
        Guid Id, DateOnly Date, int Minutes, string? Note,
        IReadOnlyList<ZeusUsageAllocationDto> Allocations,
        TimeOnly? StartTime = null, TimeOnly? EndTime = null);

    public sealed record ZeusSummaryDto(
        long PurchasedMinutes, long UsedMinutes, long AvailableMinutes,
        decimal TotalSpent, IReadOnlyList<ZeusPassDto> Passes, IReadOnlyList<ZeusUsageDto> Usages,
        IReadOnlyList<ZeusCareStayDto> CareStays);
}
