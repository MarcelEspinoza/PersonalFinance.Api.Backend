using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Cuenta real donde entra y sale el dinero.
    /// </summary>
    public class Account
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public AccountType Type { get; set; } = AccountType.Checking;

        public string Currency { get; set; } = "EUR";

        /// <summary>
        /// Metadatos opcionales heredados del antiguo concepto "Banco": entidad,
        /// número de cuenta y color de UI. Viven aquí para que exista una sola
        /// lista de cuentas en toda la app; el módulo legado (Gastos/Ingresos)
        /// sigue leyendo la tabla Banks, que se mantiene sincronizada desde aquí.
        /// </summary>
        public string? Entity { get; set; }
        public string? AccountNumber { get; set; }
        public string? Color { get; set; }

        /// <summary>Saldo conocido en <see cref="OpeningDate"/>; ancla el arrastre mensual.</summary>
        public decimal OpeningBalance { get; set; }

        public DateOnly OpeningDate { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<LedgerEntry> Entries { get; set; } = new List<LedgerEntry>();
    }
}
