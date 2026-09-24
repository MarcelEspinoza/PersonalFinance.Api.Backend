using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    /// <summary>
    /// Cuadro mensual completo: reproduce la hoja MONTH del Excel, con el
    /// arrastre del mes anterior arriba y los subtotales por grupo.
    /// </summary>
    public class MonthlySummaryDto
    {
        public Guid PeriodId { get; set; }
        public int Year { get; set; }
        public int Month { get; set; }
        public PeriodStatus Status { get; set; }

        /// <summary>Lo que sobró del mes anterior.</summary>
        public decimal CarryOverAmount { get; set; }

        public List<MonthlyGroupDto> IncomeGroups { get; set; } = new();
        public List<MonthlyGroupDto> ExpenseGroups { get; set; } = new();

        public MonthlyTotalsDto Totals { get; set; } = new();
    }

    public class MonthlyGroupDto
    {
        public Guid GroupId { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptKind Kind { get; set; }
        public int SortOrder { get; set; }

        public decimal ForecastTotal { get; set; }
        public decimal ActualTotal { get; set; }
        public decimal RemainingTotal { get; set; }

        public List<MonthlyConceptDto> Concepts { get; set; } = new();
    }

    public class MonthlyConceptDto
    {
        public Guid ConceptId { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptNature Nature { get; set; }
        public int SortOrder { get; set; }

        public decimal ForecastTotal { get; set; }
        public decimal ActualTotal { get; set; }
        public decimal RemainingTotal { get; set; }

        /// <summary>Tope presupuestado del mes, si el concepto tiene uno.</summary>
        public decimal? BudgetLimit { get; set; }

        /// <summary>True cuando lo gastado supera el tope presupuestado.</summary>
        public bool IsOverBudget { get; set; }

        public List<MonthlyEntryDto> Entries { get; set; } = new();
    }

    public class MonthlyEntryDto
    {
        public Guid Id { get; set; }
        public EntryDirection Direction { get; set; }
        public EntryStatus Status { get; set; }
        public DateOnly DueDate { get; set; }
        public DateOnly? ValueDate { get; set; }
        public decimal ForecastAmount { get; set; }
        public decimal? ActualAmount { get; set; }
        public decimal Remaining { get; set; }
        public string? Description { get; set; }
        public Guid? AccountId { get; set; }
        public string? AccountName { get; set; }
        public bool FromRecurringRule { get; set; }
    }

    public class MonthlyTotalsDto
    {
        public decimal IncomeForecast { get; set; }
        public decimal IncomeActual { get; set; }
        public decimal ExpenseForecast { get; set; }
        public decimal ExpenseActual { get; set; }

        /// <summary>Arrastre + ingresos previstos − gastos previstos.</summary>
        public decimal ProjectedBalance { get; set; }

        /// <summary>Arrastre + ingresos reales − gastos reales: el dinero que hay de verdad hoy.</summary>
        public decimal ActualBalance { get; set; }

        /// <summary>Gasto previsto que aún no se ha ejecutado.</summary>
        public decimal PendingExpense { get; set; }

        /// <summary>Ingreso previsto que aún no ha entrado.</summary>
        public decimal PendingIncome { get; set; }
    }
}
