namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Una cuota del cuadro de amortización francés: cuánto va a capital,
    /// cuánto a intereses y qué capital queda vivo después de pagarla.
    /// </summary>
    public class DebtScheduleItem
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid DebtId { get; set; }
        public Debt? Debt { get; set; }

        /// <summary>Número de cuota, empezando en 1.</summary>
        public int InstallmentNumber { get; set; }

        public DateOnly DueDate { get; set; }

        public decimal InstallmentAmount { get; set; }
        public decimal PrincipalPortion { get; set; }
        public decimal InterestPortion { get; set; }

        /// <summary>Capital pendiente tras aplicar esta cuota.</summary>
        public decimal RemainingPrincipal { get; set; }

        public bool IsPaid { get; set; }
        public DateOnly? PaidDate { get; set; }

        /// <summary>Importe realmente pagado, si difiere de la cuota teórica.</summary>
        public decimal? PaidAmount { get; set; }
    }
}
