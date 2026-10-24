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
        public ConceptKind Kind { get; set; }
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
        public RecurrenceFrequency? Frequency { get; set; }
        public DateOnly? NextDueMonth { get; set; }
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

        /// <summary>
        /// Solo para fijos. Si no llega, se conserva la frecuencia que ya tenía.
        /// </summary>
        public RecurrenceFrequency? Frequency { get; set; }

        /// <summary>
        /// Para "cada 2 meses": mes del próximo recibo (se ignora el día).
        /// </summary>
        public DateOnly? FirstDueMonth { get; set; }
    }

    /// <summary>Fixed and variable expenses month by month, with the same month totals as the dashboard.</summary>
    public sealed class ExpensePlanMonthsDto
    {
        public List<ExpensePlanMonthDto> Months { get; set; } = new();
        public List<ExpensePlanFixedRowDto> Fixed { get; set; } = new();
        public List<ExpensePlanVariableRowDto> Variable { get; set; } = new();
    }

    public sealed class ExpensePlanMonthDto
    {
        public int Year { get; set; }
        public int Month { get; set; }
        public bool IsCurrent { get; set; }
        public bool IsClosed { get; set; }
        public decimal Income { get; set; }
        public decimal FixedExpense { get; set; }
        public decimal VariableExpense { get; set; }
    }

    public sealed class ExpensePlanFixedRowDto
    {
        public Guid ConceptId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? GroupName { get; set; }
        public List<ExpensePlanFixedCellDto> Cells { get; set; } = new();
    }

    public sealed class ExpensePlanFixedCellDto
    {
        public decimal Amount { get; set; }

        /// <summary>Everything planned for the month is already paid.</summary>
        public bool Paid { get; set; }
    }

    public sealed class ExpensePlanVariableRowDto
    {
        public Guid ConceptId { get; set; }
        public string Name { get; set; } = string.Empty;
        public string? GroupName { get; set; }
        public decimal? DefaultBudget { get; set; }
        public List<ExpensePlanVariableCellDto> Cells { get; set; } = new();
    }

    public sealed class ExpensePlanVariableCellDto
    {
        /// <summary>The month's own limit when it has one, otherwise the usual limit.</summary>
        public decimal? Budget { get; set; }
        public bool IsOverride { get; set; }
        public decimal Spent { get; set; }

        /// <summary>Paid plus still pending entries of the month.</summary>
        public decimal Planned { get; set; }

        /// <summary>What the forecast counts: the larger of planned and budget.</summary>
        public decimal Counted { get; set; }
    }

    public sealed class UpdateExpenseBudgetDto
    {
        /// <summary>Null removes the limit.</summary>
        public decimal? Limit { get; set; }
    }
}
