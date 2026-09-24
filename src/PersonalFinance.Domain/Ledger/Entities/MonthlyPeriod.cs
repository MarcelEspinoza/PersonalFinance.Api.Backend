using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Mes contable. Guarda el arrastre de saldo entre meses
    /// (el "Last Month remaining" del Excel) y el estado de cierre.
    /// </summary>
    public class MonthlyPeriod
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }

        public PeriodStatus Status { get; set; } = PeriodStatus.Open;

        /// <summary>Saldo de cierre del mes anterior; punto de partida de este.</summary>
        public decimal CarryOverAmount { get; set; }

        /// <summary>Saldo real al cerrar. Null mientras el periodo siga abierto.</summary>
        public decimal? ClosingBalance { get; set; }

        public DateTime? ClosedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();

        /// <summary>Primer día del mes, para comparaciones de rango.</summary>
        public DateOnly FirstDay => new(Year, Month, 1);
    }
}
