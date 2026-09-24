using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Préstamo o financiación con un banco (hoja Deudas: ING, BBVA, OpenBank,
    /// Affinity, Cetelem). El cuadro de amortización vive en DebtScheduleItem.
    /// </summary>
    public class Debt
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public string? Lender { get; set; }

        public DebtKind Kind { get; set; } = DebtKind.Loan;

        public DebtStatus Status { get; set; } = DebtStatus.Active;

        /// <summary>Importe financiado al inicio.</summary>
        public decimal PrincipalAmount { get; set; }

        /// <summary>Tipo de interés nominal anual, en porcentaje (ej. 7.95).</summary>
        public decimal AnnualNominalRate { get; set; }

        /// <summary>TAE en porcentaje. Informativa; no interviene en el cuadro.</summary>
        public decimal? AnnualEquivalentRate { get; set; }

        public int TermMonths { get; set; }

        /// <summary>Cuota mensual pactada.</summary>
        public decimal InstallmentAmount { get; set; }

        public DateOnly StartDate { get; set; }

        public int PaymentDayOfMonth { get; set; } = 1;

        /// <summary>Concepto de gasto al que se imputan las cuotas.</summary>
        public Guid? ConceptId { get; set; }
        public Concept? Concept { get; set; }

        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<DebtScheduleItem> Schedule { get; set; } = new List<DebtScheduleItem>();
    }
}
