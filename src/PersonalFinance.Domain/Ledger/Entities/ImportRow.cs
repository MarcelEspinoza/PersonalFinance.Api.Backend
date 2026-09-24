using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Movimiento leído del fichero, en cuarentena hasta que el usuario lo acepta.
    /// El texto del extracto es dato no confiable: nunca se ejecuta ni se
    /// interpreta como instrucción al llamar al modelo.
    /// </summary>
    public class ImportRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid BatchId { get; set; }
        public ImportBatch? Batch { get; set; }

        public int RowNumber { get; set; }

        public DateOnly ValueDate { get; set; }

        /// <summary>Importe con signo tal y como viene del extracto.</summary>
        public decimal Amount { get; set; }

        public string? Currency { get; set; }

        public string RawDescription { get; set; } = string.Empty;

        /// <summary>Descripción en mayúsculas y sin ruido, usada para casar con ConceptMapping.</summary>
        public string? NormalizedDescription { get; set; }

        public string Fingerprint { get; set; } = string.Empty;

        public ImportRowStatus Status { get; set; } = ImportRowStatus.Pending;

        /// <summary>Concepto propuesto por la cascada de categorización.</summary>
        public Guid? SuggestedConceptId { get; set; }
        public Concept? SuggestedConcept { get; set; }

        /// <summary>Concepto elegido por el usuario. Manda sobre la sugerencia.</summary>
        public Guid? ConfirmedConceptId { get; set; }

        /// <summary>Quién propuso el concepto: mapping, ai o user.</summary>
        public string? SuggestionSource { get; set; }

        /// <summary>Confianza de la sugerencia entre 0 y 1.</summary>
        public decimal? SuggestionConfidence { get; set; }

        /// <summary>Asiento creado al aplicar el lote.</summary>
        public Guid? LedgerEntryId { get; set; }
    }
}
