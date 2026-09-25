namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ConceptMappingTemplate(
        string Pattern,
        string ConceptName,
        string? AccountName,
        int Priority);

    /// <summary>
    /// Reglas verificadas contra los extractos Revolut de las cuentas personal
    /// y conjunta. Los patrones ya están en la forma producida por
    /// <see cref="RevolutMovementClassifier.Normalize"/>.
    /// </summary>
    public static class ConceptMappingTemplates
    {
        public static IReadOnlyList<ConceptMappingTemplate> Rules { get; } =
            new List<ConceptMappingTemplate>
            {
                new(
                    "JENNY MABEL ESPINOZA SEJAS & MARCEL ORLANDO UGARTE ESPINOZA",
                    "Movimiento entre mis bancos",
                    "Personal",
                    200),
                new(
                    "MARCEL ORLANDO UGARTE ESPINOZA",
                    "Movimiento entre mis bancos",
                    "Conjunta",
                    200),
                new("POCKET", "Hucha y fondos Revolut", "Personal", 100),
                new("FLEXIBLE CASH FUNDS", "Hucha y fondos Revolut", "Personal", 100),
                new(
                    "UNA RECARGA DE APPLE PAY CON",
                    "Recarga desde mis tarjetas",
                    null,
                    100),
                new(
                    "TO MARCEL UGARTE ESPINOZA",
                    "Movimiento entre mis bancos",
                    "Personal",
                    100),
                new("PAGO DE WOLTERS KLUWER", "My Salary", "Personal", 100),
                new(
                    "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                    "Loans Done",
                    "Personal",
                    90),
                new(
                    "TRANSFERENCIA A JENNY MABEL ESPINOZA SEJAS",
                    "Loans TBP (Paid)",
                    "Personal",
                    90),
                new(
                    "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                    "Income 2 (Mama Jenny)",
                    "Conjunta",
                    90)
            };
    }
}
