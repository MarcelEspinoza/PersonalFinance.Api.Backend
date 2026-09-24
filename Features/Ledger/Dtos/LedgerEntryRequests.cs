using System.ComponentModel.DataAnnotations;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Dtos
{
    public class CreateLedgerEntryDto
    {
        [Required]
        public Guid ConceptId { get; set; }

        public Guid? AccountId { get; set; }

        [Required]
        public EntryDirection Direction { get; set; }

        [Required]
        public DateOnly DueDate { get; set; }

        [Range(0, 9_999_999_999_999_999.99)]
        public decimal ForecastAmount { get; set; }

        /// <summary>Si viene informado, el asiento nace ya confirmado.</summary>
        [Range(0, 9_999_999_999_999_999.99)]
        public decimal? ActualAmount { get; set; }

        public DateOnly? ValueDate { get; set; }

        [MaxLength(300)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    public class UpdateLedgerEntryDto
    {
        public Guid? ConceptId { get; set; }
        public Guid? AccountId { get; set; }
        public DateOnly? DueDate { get; set; }

        [Range(0, 9_999_999_999_999_999.99)]
        public decimal? ForecastAmount { get; set; }

        [MaxLength(300)]
        public string? Description { get; set; }

        [MaxLength(500)]
        public string? Notes { get; set; }
    }

    /// <summary>Marca un asiento como pagado o cobrado con su importe real.</summary>
    public class ConfirmLedgerEntryDto
    {
        [Required]
        [Range(0, 9_999_999_999_999_999.99)]
        public decimal ActualAmount { get; set; }

        public DateOnly? ValueDate { get; set; }

        public Guid? AccountId { get; set; }
    }

    public class CloseMonthDto
    {
        /// <summary>
        /// Saldo real de la cuenta al cerrar. Si se informa, manda sobre el
        /// calculado y se convierte en el arrastre del mes siguiente.
        /// </summary>
        public decimal? ActualClosingBalance { get; set; }
    }
}
