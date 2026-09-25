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
    /// Plan de cuentas tal y como está en el Excel "Personal accounts 2018 V4"
    /// (hojas Fixed y Variables). Se mantienen los nombres literales, incluidos
    /// los huecos numerados, para que la importación de los meses históricos
    /// pueda casar cada fila de la hoja con su concepto.
    /// </summary>
    public static class ChartOfAccountsTemplate
    {
        public static IReadOnlyList<ConceptGroupTemplate> Groups { get; } = new List<ConceptGroupTemplate>
        {
            new("INCOMES", ConceptKind.Income, 10, new List<ConceptTemplate>
            {
                new("My Salary", ConceptNature.Fixed, 10),
                new("Income 2 (Mama Jenny)", ConceptNature.Fixed, 20),
                new("Additional Income 2 (Vane)", ConceptNature.Variable, 30),
                new("Additional Income 3", ConceptNature.Variable, 40),
                new("Additional Income 4", ConceptNature.Variable, 50),
                new("Additional Income 5", ConceptNature.Variable, 60),
                new("Loans TBP", ConceptNature.Variable, 70),
                new("Loans Done", ConceptNature.Variable, 80)
            }),

            new("Apartment costs", ConceptKind.Expense, 10, new List<ConceptTemplate>
            {
                // "Rent Aparment" está así escrito en el Excel; se respeta el
                // literal para no romper el casado de las filas históricas.
                new("Rent Aparment", ConceptNature.Fixed, 10),
                new("Electric Bill", ConceptNature.Fixed, 20),
                new("Water Bill", ConceptNature.Fixed, 30),
                new("Gas Bill", ConceptNature.Fixed, 40),
                new("Internet + Mobile phone and Landline", ConceptNature.Fixed, 50)
            }),

            new("Extra Payments", ConceptKind.Expense, 20, new List<ConceptTemplate>
            {
                new("Loans TBP (Paid)", ConceptNature.Variable, 10),
                new("Loans TBP (Pending)", ConceptNature.Variable, 20),
                new("Extra 3", ConceptNature.Variable, 30),
                new("Extra 4", ConceptNature.Variable, 40),
                new("Extra 5", ConceptNature.Variable, 50),
                new("Extra 6", ConceptNature.Variable, 60),
                new("Extra 7", ConceptNature.Variable, 70)
            }),

            new("Extra Fixed Payments", ConceptKind.Expense, 30, new List<ConceptTemplate>
            {
                new("Extra Fixed 1", ConceptNature.Fixed, 10),
                new("Extra Fixed 2", ConceptNature.Fixed, 20),
                new("Extra Fixed 3", ConceptNature.Fixed, 30),
                new("Extra Fixed 4", ConceptNature.Fixed, 40),
                new("Extra Fixed 5", ConceptNature.Fixed, 50),
                new("Extra Fixed 6", ConceptNature.Fixed, 60),
                new("Extra Fixed 7", ConceptNature.Fixed, 70),
                new("Extra Fixed 8", ConceptNature.Fixed, 80),
                new("Pasanaco", ConceptNature.Fixed, 90)
            }),

            new("Subscriptions", ConceptKind.Expense, 40, new List<ConceptTemplate>
            {
                new("Netflix", ConceptNature.Fixed, 10),
                new("Disney+", ConceptNature.Fixed, 20),
                new("Spotify", ConceptNature.Fixed, 30)
            }),

            // Presupuestos de la hoja Variables (celdas FB / PC / Transp).
            new("Variable Costs", ConceptKind.Expense, 50, new List<ConceptTemplate>
            {
                new("Food & Beverage", ConceptNature.Variable, 10, 230m),
                new("Personal costs", ConceptNature.Variable, 20, 150m),
                new("Transport", ConceptNature.Variable, 30, 25m),
                // El Excel no la tenía porque allí no se veían las comisiones.
                // El extracto sí las trae, y escondidas dentro de otro gasto
                // no hay forma de saber cuánto cuesta el banco al año.
                new("Bank fees", ConceptNature.Variable, 40)
            }),

            new("Savings", ConceptKind.Expense, 60, new List<ConceptTemplate>
            {
                new("Savings", ConceptNature.Fixed, 10)
            }),

            // Grupo nuevo, ajeno al Excel: dinero que cambia de sitio sin
            // dejar de ser tuyo. No suma en gastos ni en ingresos, pero sí
            // mueve el saldo, que es lo que hace que cuadre con el banco.
            new("Traspasos", ConceptKind.Transfer, 70, new List<ConceptTemplate>
            {
                new("Hucha y fondos Revolut", ConceptNature.Variable, 10),
                new("Recarga desde mis tarjetas", ConceptNature.Variable, 20),
                new("Movimiento entre mis bancos", ConceptNature.Variable, 30)
            })
        };
    }
}
