using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Models.Dtos.Dashboard
{
    /// <summary>
    /// Previsión del mes seleccionado: qué falta por cobrar y pagar, cómo
    /// terminará cada cuenta y qué movimientos conviene hacer para evitar
    /// descubiertos.
    /// </summary>
    public class MonthOutlookDto
    {
        public int Year { get; set; }
        public int Month { get; set; }

        /// <summary>El mes ya terminó: los saldos son reales y lo pendiente está vencido.</summary>
        public bool IsPast { get; set; }

        /// <summary>El mes contiene el día de hoy.</summary>
        public bool IsCurrent { get; set; }

        public List<AccountOutlookDto> Accounts { get; set; } = new();
        public UnassignedOutlookDto Unassigned { get; set; } = new();
        public List<OutlookItemDto> PendingItems { get; set; } = new();
        public List<OutlookItemDto> OverdueItems { get; set; } = new();
        public List<TransferSuggestionDto> SuggestedTransfers { get; set; } = new();

        /// <summary>Déficit que ninguna otra cuenta puede cubrir.</summary>
        public decimal UncoveredShortfall { get; set; }

        /// <summary>Lo que quedará libre tras cubrir todo lo pendiente y las reservas variables.</summary>
        public decimal FreeMoney { get; set; }

        /// <summary>Reserva variable total, incluida la asignada a cuentas.</summary>
        public decimal VariableExpenseReserve { get; set; }

        public List<ConceptDeviationDto> Deviations { get; set; } = new();
    }

    public class AccountOutlookDto
    {
        public Guid AccountId { get; set; }
        public string Name { get; set; } = string.Empty;

        /// <summary>Saldo de partida de la previsión (hoy, o el previsto al empezar un mes futuro).</summary>
        public decimal BaseBalance { get; set; }

        public decimal PendingIncome { get; set; }
        public decimal PendingExpense { get; set; }
        public decimal ProjectedEndBalance { get; set; }

        /// <summary>Saldo más bajo que alcanzará la cuenta durante el mes.</summary>
        public decimal LowestBalance { get; set; }
        public DateOnly? LowestBalanceDate { get; set; }

        /// <summary>Dinero que falta para que la cuenta no quede en negativo.</summary>
        public decimal Shortfall { get; set; }

        /// <summary>Saldo real de la cuenta al terminar el mes o, si sigue en curso, hoy.</summary>
        public decimal ActualBalance { get; set; }

        /// <summary>Saldo bancario guardado en el cuadre de este mes.</summary>
        public decimal? ReconciledBalance { get; set; }
        public bool IsReconciled { get; set; }
    }

    public class UnassignedOutlookDto
    {
        public decimal PendingIncome { get; set; }
        public decimal PendingExpense { get; set; }

        /// <summary>Lo que aún queda por gastar de los límites variables.</summary>
        public decimal VariableExpenseReserve { get; set; }

        /// <summary>Lo que aún se espera cobrar de las estimaciones variables.</summary>
        public decimal VariableIncomeExpected { get; set; }
    }

    public class OutlookItemDto
    {
        public DateOnly DueDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ConceptName { get; set; } = string.Empty;
        public Guid? AccountId { get; set; }
        public string? AccountName { get; set; }
        public EntryDirection Direction { get; set; }
        public decimal Amount { get; set; }
        public bool IsOverdue { get; set; }
        public bool IsTransfer { get; set; }
        public bool IsVariableReserve { get; set; }
    }

    public class TransferSuggestionDto
    {
        public Guid FromAccountId { get; set; }
        public string FromAccountName { get; set; } = string.Empty;
        public Guid ToAccountId { get; set; }
        public string ToAccountName { get; set; } = string.Empty;
        public decimal Amount { get; set; }

        /// <summary>Fecha en la que la cuenta destino quedaría en negativo.</summary>
        public DateOnly? Before { get; set; }
    }

    public class ConceptDeviationDto
    {
        public Guid ConceptId { get; set; }
        public string Name { get; set; } = string.Empty;
        public ConceptKind Kind { get; set; }
        public ConceptNature Nature { get; set; }
        public decimal Planned { get; set; }
        public decimal Actual { get; set; }
        public decimal Pending { get; set; }

        /// <summary>Real + pendiente − planificado. Positivo en gastos significa gastar de más.</summary>
        public decimal Deviation { get; set; }
    }

    public class DashboardProjectionResult
    {
        public List<MonthlyProjectionDto> MonthlyData { get; set; } = new();
        public SummaryDto Summary { get; set; } = new();
        public DashboardAlertsDto Alerts { get; set; } = new();
        public List<DashboardAccountBalanceDto> Accounts { get; set; } = new();
        public MonthOutlookDto Outlook { get; set; } = new();
        public PeriodStateDto Period { get; set; } = new();

        /// <summary>Mes que la app considera el actual (el inicio contable manda sobre el calendario).</summary>
        public int DefaultYear { get; set; }
        public int DefaultMonth { get; set; }

        /// <summary>Primer mes con contabilidad válida; antes no hay nada que consultar.</summary>
        public int MinYear { get; set; }
        public int MinMonth { get; set; }
    }

    public class PeriodStateDto
    {
        /// <summary>"notOpened", "open" o "closed".</summary>
        public string Status { get; set; } = "notOpened";
        public DateTime? ClosedAt { get; set; }
        public decimal? ClosingBalance { get; set; }
    }
}
