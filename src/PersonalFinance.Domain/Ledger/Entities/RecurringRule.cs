using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Plantilla que materializa asientos Planned al abrir un mes.
    /// Equivale a las filas fijas del Excel que se repiten cada mes
    /// (alquiler, seguros, suscripciones, nómina).
    /// </summary>
    public class RecurringRule
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid ConceptId { get; set; }
        public Concept? Concept { get; set; }

        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        public string? Description { get; set; }

        public EntryDirection Direction { get; set; }

        public RecurrenceFrequency Frequency { get; set; } = RecurrenceFrequency.Monthly;

        /// <summary>Día del mes en que se espera el cargo o abono (1-31, recortado a fin de mes).</summary>
        public int DayOfMonth { get; set; } = 1;

        public decimal ForecastAmount { get; set; }

        public DateOnly StartDate { get; set; }

        /// <summary>Null para reglas sin fecha de fin.</summary>
        public DateOnly? EndDate { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
    }
}
