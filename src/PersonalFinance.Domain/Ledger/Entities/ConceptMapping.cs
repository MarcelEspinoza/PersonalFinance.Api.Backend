
namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Regla aprendida "texto del extracto -&gt; concepto". Se consulta antes de
    /// llamar al modelo, así que cada acierto evita una llamada de pago.
    /// </summary>
    public class ConceptMapping
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        /// <summary>
        /// Cuenta a la que se limita la regla. Null = vale para todas.
        /// Hace falta porque un mismo texto significa cosas distintas según
        /// dónde caiga: "Transferencia de JENNY MABEL ESPINOZA SEJAS" es una
        /// devolución de préstamo en la cuenta personal y una aportación a los
        /// gastos en la conjunta.
        /// </summary>
        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>Texto normalizado a buscar dentro de la descripción del movimiento.</summary>
        public string Pattern { get; set; } = string.Empty;

        public Guid ConceptId { get; set; }
        public Concept? Concept { get; set; }

        /// <summary>Reglas con prioridad mayor se evalúan antes.</summary>
        public int Priority { get; set; }

        /// <summary>Veces que la regla ha acertado; sirve para depurar las inútiles.</summary>
        public int TimesApplied { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? LastUsedAt { get; set; }
    }
}
