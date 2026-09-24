using Microsoft.Extensions.Logging;
using PersonalFinance.Api.Common.Interfaces;

namespace PersonalFinance.Api.Common.Services
{
    public class LedgerAuditLog : ILedgerAuditLog
    {
        private readonly ILogger<LedgerAuditLog> _logger;

        public LedgerAuditLog(ILogger<LedgerAuditLog> logger) => _logger = logger;

        public void ClosingBalanceMismatch(Guid userId, int year, int month, decimal declared, decimal computed) =>
            _logger.LogWarning(
                "Descuadre al cerrar {Year}-{Month:D2} del usuario {UserId}: declarado {Declared}, calculado {Computed}, diferencia {Difference}.",
                year, month, userId, declared, computed, declared - computed);
    }
}
