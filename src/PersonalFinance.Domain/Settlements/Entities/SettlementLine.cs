namespace PersonalFinance.Domain.Settlements.Entities
{
    /// <summary>
    /// Línea de una liquidación. Puede venir de un movimiento del libro o
    /// escribirse a mano.
    /// </summary>
    public class SettlementLine
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SettlementId { get; set; }

        public Settlement? Settlement { get; set; }

        public SettlementLineKind Kind { get; set; } = SettlementLineKind.Charge;

        /// <summary>Texto que se ve en el mensaje: "Casa agosto", "Disney+".</summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>Importe que se le imputa a ella. Siempre positivo.</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// Importe total del gasto cuando se reparte. Null si le corresponde
        /// entero. Sirve para escribir "51 € copagos (102 € total)".
        /// </summary>
        public decimal? FullAmount { get; set; }

        /// <summary>Movimiento del libro que originó la línea, si lo hubo.</summary>
        public Guid? LedgerEntryId { get; set; }

        public int SortOrder { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>Cuánto suma o resta esta línea al total pendiente.</summary>
        public decimal SignedAmount =>
            Kind == SettlementLineKind.Charge ? Amount : -Amount;

        /// <summary>True si el gasto se repartió y conviene enseñar el total.</summary>
        public bool IsShared => FullAmount.HasValue && FullAmount.Value != Amount;
    }
}
