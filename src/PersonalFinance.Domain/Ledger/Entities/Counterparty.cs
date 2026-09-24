using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Domain.Ledger.Entities
{
    /// <summary>
    /// Persona o entidad con la que hay dinero de por medio: destinatarios de
    /// envíos, huchas por persona de la hoja Savings, deudores del Pasanaco.
    /// </summary>
    public class Counterparty
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid UserId { get; set; }

        public string Name { get; set; } = string.Empty;

        public CounterpartyKind Kind { get; set; } = CounterpartyKind.Person;

        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public ICollection<PersonalLoan> Loans { get; set; } = new List<PersonalLoan>();
    }
}
