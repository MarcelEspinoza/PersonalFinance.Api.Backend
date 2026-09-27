using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Common
{
    public sealed record ConceptTemplate(
        string Name,
        ConceptNature Nature,
        int SortOrder,
        decimal? DefaultMonthlyBudget = null);

    public sealed record ConceptGroupTemplate(
        string Name,
        ConceptKind Kind,
        int SortOrder,
        IReadOnlyList<ConceptTemplate> Concepts);

    /// <summary>
    /// Plan personal orientado a entender en qué se gana y se gasta el dinero.
    /// Los nombres del Excel se normalizan antes de sembrar esta plantilla para
    /// conservar los identificadores y todos los movimientos históricos.
    /// </summary>
    public static class ChartOfAccountsTemplate
    {
        public static IReadOnlyList<ConceptGroupTemplate> Groups { get; } = new List<ConceptGroupTemplate>
        {
            new("Ingresos", ConceptKind.Income, 10, new List<ConceptTemplate>
            {
                new("Nómina", ConceptNature.Fixed, 10),
                new("Aportaciones familiares", ConceptNature.Fixed, 20),
                new("Ingresos adicionales", ConceptNature.Variable, 30),
                new("Reembolsos y devoluciones", ConceptNature.Variable, 40),
                new("Préstamos cobrados", ConceptNature.Variable, 50)
            }),

            new("Hogar", ConceptKind.Expense, 10, new List<ConceptTemplate>
            {
                new("Alquiler", ConceptNature.Fixed, 10),
                new("Electricidad", ConceptNature.Fixed, 20),
                new("Agua", ConceptNature.Fixed, 30),
                new("Gas", ConceptNature.Fixed, 40),
                new("Internet y telefonía", ConceptNature.Fixed, 50),
                new("Seguridad del hogar", ConceptNature.Fixed, 60),
                new("Seguro del hogar", ConceptNature.Fixed, 70),
                new("Mantenimiento y suministros del hogar", ConceptNature.Variable, 80)
            }),

            new("Alimentación", ConceptKind.Expense, 20, new List<ConceptTemplate>
            {
                new("Supermercado y alimentación del hogar", ConceptNature.Variable, 10, 300m),
                new("Restaurantes y cafeterías", ConceptNature.Variable, 20, 150m)
            }),

            new("Salud y cuidado personal", ConceptKind.Expense, 30, new List<ConceptTemplate>
            {
                new("Seguro médico", ConceptNature.Fixed, 10),
                new("Farmacia y salud", ConceptNature.Variable, 20),
                new("Cuidado personal", ConceptNature.Variable, 30)
            }),

            new("Transporte y viajes", ConceptKind.Expense, 40, new List<ConceptTemplate>
            {
                new("Transporte diario", ConceptNature.Variable, 10, 75m),
                new("Viajes y alojamiento", ConceptNature.Variable, 20),
                new("Vehículo", ConceptNature.Variable, 30)
            }),

            new("Vida personal", ConceptKind.Expense, 50, new List<ConceptTemplate>
            {
                new("Compras personales", ConceptNature.Variable, 10, 150m),
                new("Ropa y calzado", ConceptNature.Variable, 20),
                new("Ocio y entretenimiento", ConceptNature.Variable, 30),
                new("Regalos y celebraciones", ConceptNature.Variable, 40),
                new("Formación", ConceptNature.Variable, 50)
            }),

            new("Suscripciones y servicios digitales", ConceptKind.Expense, 60, new List<ConceptTemplate>
            {
                new("Netflix", ConceptNature.Fixed, 10),
                new("Disney+", ConceptNature.Fixed, 20),
                new("Spotify", ConceptNature.Fixed, 30),
                new("Software y servicios digitales", ConceptNature.Fixed, 40)
            }),

            new("Finanzas y compromisos", ConceptKind.Expense, 70, new List<ConceptTemplate>
            {
                new("Préstamos entregados", ConceptNature.Variable, 10),
                new("Cuotas de préstamos", ConceptNature.Fixed, 20),
                new("Pasanaco", ConceptNature.Fixed, 30),
                new("Comisiones bancarias", ConceptNature.Variable, 40),
                new("Impuestos y tasas", ConceptNature.Variable, 50),
                new("Otros seguros", ConceptNature.Fixed, 60)
            }),

            new("Ahorro e inversión", ConceptKind.Expense, 80, new List<ConceptTemplate>
            {
                new("Ahorro e inversión", ConceptNature.Fixed, 10)
            }),

            new("Traspasos", ConceptKind.Transfer, 90, new List<ConceptTemplate>
            {
                new("Hucha y fondos Revolut", ConceptNature.Variable, 10),
                new("Recarga desde mis tarjetas", ConceptNature.Variable, 20),
                new("Movimiento entre mis bancos", ConceptNature.Variable, 30)
            })
        };
    }
}
