using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Asiento del libro mayor. Unifica Income, Expense y la parte de ejecución
    /// del presupuesto: cada línea lleva su importe previsto y su importe real.
    /// </summary>
    public class LedgerEntry
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid PeriodId { get; set; }
        public MonthlyPeriod? Period { get; set; }

        public Guid ConceptId { get; set; }
        public Concept? Concept { get; set; }

        /// <summary>Null mientras la línea sea una previsión sin movimiento real asociado.</summary>
        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>Regla recurrente que generó la línea, si la hubo.</summary>
        public Guid? RecurringRuleId { get; set; }
        public RecurringRule? RecurringRule { get; set; }

        public EntryDirection Direction { get; set; }

        /// <summary>
        /// Traspaso entre cuentas propias. Mueve el saldo pero no cuenta como
        /// gasto ni como ingreso: sumarlo contaría dos veces el mismo dinero,
        /// una al salir de la cuenta y otra al volver.
        /// </summary>
        public bool IsTransfer { get; set; }

        public EntryStatus Status { get; set; } = EntryStatus.Planned;

        /// <summary>Fecha prevista de cargo o abono.</summary>
        public DateOnly DueDate { get; set; }

        /// <summary>Fecha real del movimiento. Null hasta que se confirma.</summary>
        public DateOnly? ValueDate { get; set; }

        public decimal ForecastAmount { get; set; }

        /// <summary>Importe real. Null mientras Status no sea Paid.</summary>
        public decimal? ActualAmount { get; set; }

        public string? Description { get; set; }

        public string? Notes { get; set; }

        /// <summary>Cuota concreta de una deuda, cuando el asiento la liquida.</summary>
        public Guid? DebtScheduleItemId { get; set; }
        public DebtScheduleItem? DebtScheduleItem { get; set; }

        public Guid? PersonalLoanId { get; set; }
        public PersonalLoan? PersonalLoan { get; set; }

        public Guid? SavingsGoalId { get; set; }
        public SavingsGoal? SavingsGoal { get; set; }

        /// <summary>Fila de importación que originó el asiento, para trazabilidad.</summary>
        public Guid? ImportRowId { get; set; }

        /// <summary>
        /// Huella del movimiento bancario (fecha + importe + concepto normalizado).
        /// Única por usuario: impide importar dos veces la misma transacción.
        /// </summary>
        public string? Fingerprint { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }

        /// <summary>Lo que aún falta por ejecutar de esta línea.</summary>
        public decimal Remaining => ForecastAmount - (ActualAmount ?? 0m);

        /// <summary>Importe que cuenta en el saldo: el real si existe, si no el previsto.</summary>
        public decimal EffectiveAmount => ActualAmount ?? ForecastAmount;

        /// <summary>Importe con signo para sumar directamente al saldo del periodo.</summary>
        public decimal SignedEffectiveAmount =>
            Direction == EntryDirection.In ? EffectiveAmount : -EffectiveAmount;
    }
}
