using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Línea del plan de cuentas: "Rent Apartment", "My Salary", "Netflix",
    /// "Food &amp; Beverage". Sustituye a la antigua Category.
    /// </summary>
    public class Concept
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid GroupId { get; set; }
        public ConceptGroup? Group { get; set; }

        public string Name { get; set; } = string.Empty;

        public ConceptKind Kind { get; set; }

        /// <summary>Fixed: importe estable mes a mes. Variable: sujeto a presupuesto.</summary>
        public ConceptNature Nature { get; set; } = ConceptNature.Fixed;

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
        public ICollection<RecurringRule> Rules { get; set; } = new List<RecurringRule>();
    }
}
