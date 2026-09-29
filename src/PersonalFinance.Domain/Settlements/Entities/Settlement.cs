namespace PersonalFinance.Domain.Settlements.Entities
{
    /// <summary>
    /// Cuenta de gastos compartidos con una persona (mamá, Vane) para un
    /// periodo: lo que adelanté por ella, lo que ella pagó por mí y lo que ya
    /// me ha ingresado. El resultado es el saldo pendiente que se le manda por
    /// WhatsApp.
    /// </summary>
    public class Settlement
    {
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid UserId { get; set; }

        /// <summary>Persona con la que se liquida.</summary>
        public Guid CounterpartyId { get; set; }

        /// <summary>Encabezado del mensaje: "CUENTAS AGOSTO/SEPTIEMBRE 2026".</summary>
        public string Title { get; set; } = string.Empty;

        /// <summary>Ventana de movimientos que cubre, para buscar candidatos.</summary>
        public DateOnly PeriodStart { get; set; }

        public DateOnly PeriodEnd { get; set; }

        public SettlementStatus Status { get; set; } = SettlementStatus.Draft;

        /// <summary>Frase final del mensaje, editable.</summary>
        public string? ClosingNote { get; set; }

        /// <summary>
        /// Saldo que venía arrastrado de la liquidación anterior. Se suma al
        /// total pendiente: así no se pierde lo que quedó a deber el mes pasado.
        /// </summary>
        public decimal CarriedOverAmount { get; set; }

        public DateTime? SentAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        public ICollection<SettlementLine> Lines { get; set; } = new List<SettlementLine>();
    }
}
