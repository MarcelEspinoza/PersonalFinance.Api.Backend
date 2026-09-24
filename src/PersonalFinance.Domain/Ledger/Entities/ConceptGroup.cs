using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Agrupación de conceptos que produce los SUBTOTAL del cuadro mensual
    /// (Apartment costs, Extra Payments, Extra Fixed Payments, Subscriptions...).
    /// </summary>
    public class ConceptGroup
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public ConceptKind Kind { get; set; }

        public int SortOrder { get; set; }

        public bool IsActive { get; set; } = true;

        public ICollection<Concept> Concepts { get; set; } = new List<Concept>();
    }
}
