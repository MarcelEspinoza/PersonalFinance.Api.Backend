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
                new("PAGO DE WOLTERS KLUWER", "Nómina", "Personal", 100),
                new(
                    "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                    "Préstamos cobrados",
                    "Personal",
                    90),
                new(
                    "TRANSFERENCIA A JENNY MABEL ESPINOZA SEJAS",
                    "Préstamos entregados",
                    "Personal",
                    90),
                new(
                    "TRANSFERENCIA DE JENNY MABEL ESPINOZA SEJAS",
                    "Aportaciones familiares",
                    "Conjunta",
                    90),
                new("MERCADONA", "Supermercado y alimentación del hogar", null, 80),
                new("LIDL", "Supermercado y alimentación del hogar", null, 80),
                new("ULFAT FRUTAS I VERDURAS", "Supermercado y alimentación del hogar", null, 80),
                new("FRUITES I VERDURES", "Supermercado y alimentación del hogar", null, 80),
                new("MCDONALD'S", "Restaurantes y cafeterías", null, 80),
                new("HECTAREA INMOBILIARIA", "Alquiler", "Conjunta", 80),
                new("SECURITAS DIRECT", "Seguridad del hogar", "Conjunta", 80),
                new("O2 FIBRA", "Internet y telefonía", "Conjunta", 80),
                new("ENI PLENITUDE", "Gas", "Conjunta", 80),
                new("AIGUES DE BARCELONA", "Agua", "Conjunta", 80),
                new("COMERCIALIZADORA", "Electricidad", "Conjunta", 80),
                new("ASISTENCIA SANITARIA", "Seguro médico", "Conjunta", 80),
                new("ASSISTENCIA SANITARIA", "Seguro médico", "Conjunta", 80),
                new("CAJA DE SEGUROS REUNIDOS", "Seguro del hogar", "Conjunta", 80),
                new("FREENOW", "Transporte diario", null, 80),
                new("TRAINLINE", "Viajes y alojamiento", null, 80),
                new("NETFLIX", "Netflix", null, 80),
                new("GOOGLE ONE", "Software y servicios digitales", null, 80),
                new("RAILWAY", "Software y servicios digitales", null, 80),
                new("AMAZON", "Compras personales", null, 70)
            };
    }
}
