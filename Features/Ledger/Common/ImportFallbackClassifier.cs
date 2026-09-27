namespace PersonalFinance.Api.Features.Ledger.Common
{
    public static class ImportFallbackClassifier
    {
        public static Guid? FindConceptId(
            string normalizedDescription,
            decimal amount,
            IReadOnlyDictionary<string, Guid> conceptIds)
        {
            var conceptName = FindConceptName(normalizedDescription, amount);
            return conceptIds.TryGetValue(conceptName, out var conceptId)
                ? conceptId
                : null;
        }

        private static string FindConceptName(string normalizedDescription, decimal amount)
        {
            if (amount >= 0)
            {
                if (normalizedDescription.Contains("REFUND", StringComparison.OrdinalIgnoreCase) ||
                    normalizedDescription.Contains("REMBOLSO", StringComparison.OrdinalIgnoreCase) ||
                    normalizedDescription.Contains("DEVOLUCION", StringComparison.OrdinalIgnoreCase))
                    return "Reembolsos y devoluciones";

                return "Ingresos adicionales";
            }

            if (normalizedDescription.Contains("RETIRADA DE EFECTIVO", StringComparison.OrdinalIgnoreCase))
                return "Retiradas de efectivo";

            if (normalizedDescription.StartsWith("TRANSFERENCIA A ", StringComparison.OrdinalIgnoreCase) ||
                normalizedDescription.StartsWith("BIZUM PAYMENT TO:", StringComparison.OrdinalIgnoreCase) ||
                normalizedDescription.StartsWith("TO ", StringComparison.OrdinalIgnoreCase))
                return "Transferencias y Bizum enviados";

            return "Compras personales";
        }
    }
}
