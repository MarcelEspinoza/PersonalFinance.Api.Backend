namespace PersonalFinance.Domain.Settlements.Entities
{
    /// <summary>Estado de la liquidación: mientras es borrador se puede editar.</summary>
    public enum SettlementStatus
    {
        Draft = 0,
        Sent = 1,
        Closed = 2
    }

    /// <summary>
    /// Papel de cada línea en la cuenta final.
    /// </summary>
    public enum SettlementLineKind
    {
        /// <summary>Gasto que adelanté y le corresponde a ella. Suma.</summary>
        Charge = 0,

        /// <summary>Algo que ella pagó por mí y descuento del total. Resta.</summary>
        Deduction = 1,

        /// <summary>Dinero que ya me ha ingresado a cuenta. Resta.</summary>
        Payment = 2
    }
}
