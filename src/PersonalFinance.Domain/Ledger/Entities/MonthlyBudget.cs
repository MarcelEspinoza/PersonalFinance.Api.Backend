namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Tope mensual de gasto de un concepto variable (hoja Variables del Excel:
    /// Supermercado 300, compras personales 150, transporte 75).
    /// </summary>
    public class MonthlyBudget
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid ConceptId { get; set; }
        public Concept? Concept { get; set; }

        public int Year { get; set; }
        public int Month { get; set; }

        public decimal LimitAmount { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
