using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    public sealed class ExpensePlanningDto
    {
        public List<ExpensePlanningGroupDto> Groups { get; set; } = new();
        public List<ExpensePlanningAccountDto> Accounts { get; set; } = new();
    }

    public sealed class ExpensePlanningGroupDto
    {
        public string Name { get; set; } = string.Empty;
        public int SortOrder { get; set; }
        public List<ExpensePlanningItemDto> Items { get; set; } = new();
    }

    public sealed class ExpensePlanningItemDto
    {
        public Guid ConceptId { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptNature Nature { get; set; }
        public decimal? MonthlyAmount { get; set; }
        public decimal? MonthlyBudget { get; set; }
        public int? DayOfMonth { get; set; }
        public Guid? AccountId { get; set; }
    }

    public sealed class ExpensePlanningAccountDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class UpdateExpensePlanningDto
    {
        public ConceptNature Nature { get; set; }
        public decimal? MonthlyAmount { get; set; }
        public decimal? MonthlyBudget { get; set; }
        public int? DayOfMonth { get; set; }
        public Guid? AccountId { get; set; }
    }
}
