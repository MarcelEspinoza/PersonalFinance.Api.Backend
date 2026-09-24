using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Dinero prestado a alguien o recibido de alguien, sin cuadro de amortización.
    /// El saldo vivo sale de los LedgerEntry asociados.
    /// </summary>
    public class PersonalLoan
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public Guid CounterpartyId { get; set; }
        public Counterparty? Counterparty { get; set; }

        public string? Description { get; set; }

        public PersonalLoanDirection Direction { get; set; }

        public decimal PrincipalAmount { get; set; }

        public DateOnly StartDate { get; set; }

        public DateOnly? ExpectedSettlementDate { get; set; }

        public bool IsSettled { get; set; }
        public DateOnly? SettledDate { get; set; }

        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
    }
}
