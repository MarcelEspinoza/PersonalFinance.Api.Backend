namespace PersonalFinance.Api.Common.Interfaces
{
    /// <summary>
    /// Avisos contables que la aplicación quiere dejar registrados sin
    /// depender del sistema de logging concreto.
    /// </summary>
    public interface ILedgerAuditLog
    {
        /// <summary>
        /// El saldo declarado al cerrar no coincide con el calculado: hay un
        /// descuadre que conviene poder rastrear después.
        /// </summary>
        void ClosingBalanceMismatch(Guid userId, int year, int month, decimal declared, decimal computed);
    }
}
