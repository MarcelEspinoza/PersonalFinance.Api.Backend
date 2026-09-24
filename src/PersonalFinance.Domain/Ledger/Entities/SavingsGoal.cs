
namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Hucha con destino concreto (Mama Jenny, Javy, Amazon, Inversion...).
    /// Los movimientos son LedgerEntry apuntando a esta meta.
    /// </summary>
    public class SavingsGoal
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        /// <summary>Persona asociada a la hucha, si aplica.</summary>
        public Guid? CounterpartyId { get; set; }
        public Counterparty? Counterparty { get; set; }

        public Guid? AccountId { get; set; }
        public Account? Account { get; set; }

        /// <summary>Objetivo a alcanzar. Null si es una hucha sin meta fijada.</summary>
        public decimal? TargetAmount { get; set; }

        public DateOnly? TargetDate { get; set; }

        /// <summary>Saldo acumulado antes de la primera entrada registrada en la app.</summary>
        public decimal OpeningBalance { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
    }
}
