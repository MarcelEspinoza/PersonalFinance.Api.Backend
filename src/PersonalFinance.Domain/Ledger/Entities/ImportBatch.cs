using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Una carga de movimientos (CSV de Revolut, Excel histórico). Nada se
    /// escribe en el libro hasta que el lote se aplica explícitamente.
    /// </summary>
    public class ImportBatch
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        public ImportSource Source { get; set; }

        public string? FileName { get; set; }

        public ImportBatchStatus Status { get; set; } = ImportBatchStatus.Draft;

        public int TotalRows { get; set; }
        public int AcceptedRows { get; set; }
        public int DuplicateRows { get; set; }
        /// <summary>Movimientos del fichero que no se importan (devueltos o reflejos de la hucha).</summary>
        public int ExcludedRows { get; set; }

        /// <summary>Saldo con el que el banco cierra el extracto: el del último movimiento completado.</summary>
        public decimal? BankBalance { get; set; }
        public DateOnly? BankBalanceDate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? AppliedAt { get; set; }

        public ICollection<ImportRow> Rows { get; set; } = new List<ImportRow>();
    }
}
