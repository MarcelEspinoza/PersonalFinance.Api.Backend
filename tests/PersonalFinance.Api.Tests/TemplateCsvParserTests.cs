using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

public class TemplateCsvParserTests
{
    [Fact]
    public void Parse_AcceptsCommaSeparatedTemplateAndQuotedValues()
    {
        const string csv =
            "description,amount,date,category,notes,type,movementType,bank,isTransfer,counterpartyBank,transferReference,loan\n" +
            "\"Compra, supermercado\",42.50,27/09/2026,Alimentación,\"Oferta \"\"semanal\"\"\",Variable,Expense,Principal,false,,,\n";

        var row = Assert.Single(TemplateCsvParser.Parse(new StringReader(csv)));

        Assert.Equal("Compra, supermercado", row.Description);
        Assert.Equal("42.50", row.Amount);
        Assert.Equal("Alimentación", row.Category);
        Assert.Equal("Oferta \"semanal\"", row.Notes);
        Assert.Equal("Expense", row.MovementType);
        Assert.Equal("Principal", row.BankOrigin);
    }

    [Fact]
    public void Parse_AcceptsSemicolonSeparatedSpanishHeaders()
    {
        const string csv =
            "Descripción;Importe;Fecha;Categoría;Notas;Tipo;Tipo movimiento;Banco origen;Transferencia;Banco destino;Referencia transferencia;Préstamo\n" +
            "Nómina;2500,75;2026-09-27;Salario;;Fixed;Income;Principal;false;;;\n";

        var row = Assert.Single(TemplateCsvParser.Parse(new StringReader(csv)));

        Assert.Equal("Nómina", row.Description);
        Assert.Equal("2500,75", row.Amount);
        Assert.Equal("2026-09-27", row.Date);
        Assert.Equal("Income", row.MovementType);
    }

    [Fact]
    public void Parse_RejectsCsvWithoutRequiredHeaders()
    {
        const string csv = "description,amount\nCompra,10\n";

        var exception = Assert.Throws<InvalidDataException>(
            () => TemplateCsvParser.Parse(new StringReader(csv)));

        Assert.Contains("columnas obligatorias", exception.Message);
    }
}
