using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Formas tomadas de un extracto real de Revolut es-ES (1.136 movimientos,
/// ene-sep 2026). Si Revolut cambia el formato, estos tests son los que avisan.
/// </summary>
public class RevolutImportTests
{
    private const string Header =
        "Tipo;Producto;Fecha de inicio;Fecha de finalización;Descripción;Importe;Comisión;Divisa;State;Saldo";

    private static RevolutParseResult Parse(params string[] lines) =>
        RevolutCsvParser.Parse(new StringReader(string.Join("\n", lines)));

    // ----------------------------------------------------------------------
    // Lectura del fichero
    // ----------------------------------------------------------------------

    [Fact]
    public void Lee_un_movimiento_corriente()
    {
        var result = Parse(Header,
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;NAVAS EXPRESS;-12.35;0.00;EUR;COMPLETADO;123.45");

        Assert.Empty(result.Problems);
        var m = Assert.Single(result.Movements);

        Assert.Equal("Pago con tarjeta", m.Type);
        Assert.Equal("Actual", m.Product);
        Assert.Equal(new DateTime(2026, 1, 5, 14, 22, 0), m.StartedAt);
        Assert.Equal("NAVAS EXPRESS", m.Description);
        Assert.Equal(-12.35m, m.Amount);
        Assert.Equal("EUR", m.Currency);
        Assert.Equal(123.45m, m.Balance);
    }

    [Fact]
    public void Acepta_la_hora_con_un_solo_digito()
    {
        // En el extracto real conviven "2:38" y "19:34".
        var result = Parse(Header,
            "Transferir;Ahorros;01/01/2026 2:38;01/01/2026 2:38;Al Pocket EUR Savings desde EUR;0.02;0.00;EUR;COMPLETADO;1.02");

        Assert.Empty(result.Problems);
        Assert.Equal(new DateTime(2026, 1, 1, 2, 38, 0), result.Movements[0].StartedAt);
    }

    [Fact]
    public void Un_movimiento_devuelto_no_trae_saldo_y_aun_asi_se_lee()
    {
        var result = Parse(Header,
            "Pago con tarjeta;Actual;04/02/2026 20:50;;ALGO QUE SE DEVOLVIO;-24.96;0.00;EUR;DEVUELTO;");

        Assert.Empty(result.Problems);
        var m = Assert.Single(result.Movements);

        Assert.Null(m.Balance);
        Assert.Null(m.CompletedAt);
        Assert.Equal("DEVUELTO", m.State);
    }

    [Fact]
    public void Las_columnas_se_localizan_por_nombre_no_por_posicion()
    {
        // Si Revolut reordena las columnas, seguimos leyendo bien. Colocar los
        // importes en el campo equivocado sería mucho peor que fallar.
        var reordered = "Descripción;Importe;Tipo;Producto;State;Divisa;Fecha de inicio;Saldo";

        var result = Parse(reordered,
            "NAVAS EXPRESS;-12.35;Pago con tarjeta;Actual;COMPLETADO;EUR;05/01/2026 14:22;123.45");

        Assert.Empty(result.Problems);
        var m = Assert.Single(result.Movements);

        Assert.Equal("NAVAS EXPRESS", m.Description);
        Assert.Equal(-12.35m, m.Amount);
        Assert.Equal(123.45m, m.Balance);
    }

    [Fact]
    public void Una_cabecera_sin_las_columnas_necesarias_se_rechaza_entera()
    {
        var result = Parse("Fecha;Concepto;Cantidad", "01/01/2026;ALGO;10");

        Assert.Empty(result.Movements);
        var problema = Assert.Single(result.Problems);
        Assert.Contains("Importe", problema);
    }

    [Fact]
    public void Un_punto_y_coma_entrecomillado_no_desplaza_las_columnas()
    {
        var result = Parse(Header,
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;\"BAR PEPE; S.L.\";-12.35;0.00;EUR;COMPLETADO;123.45");

        Assert.Empty(result.Problems);
        var m = Assert.Single(result.Movements);

        Assert.Equal("BAR PEPE; S.L.", m.Description);
        Assert.Equal(-12.35m, m.Amount);
    }

    [Fact]
    public void Una_linea_ilegible_no_tumba_el_resto_del_fichero()
    {
        var result = Parse(Header,
            "Pago con tarjeta;Actual;no-es-una-fecha;;ROTA;-1.00;0.00;EUR;COMPLETADO;1.00",
            "Pago con tarjeta;Actual;05/01/2026 14:22;05/01/2026 14:22;BUENA;-12.35;0.00;EUR;COMPLETADO;123.45");

        Assert.Single(result.Movements);
        Assert.Equal("BUENA", result.Movements[0].Description);

        var problema = Assert.Single(result.Problems);
        Assert.Contains("Línea 2", problema);
    }

    // ----------------------------------------------------------------------
    // Reglas estructurales
    // ----------------------------------------------------------------------

    private static ClassifiedMovement Classify(
        string product = "Actual",
        string state = "COMPLETADO",
        decimal amount = -10m,
        string description = "ALGO",
        decimal fee = 0m) =>
        RevolutMovementClassifier.Classify(new RevolutMovement(
            2, "Pago con tarjeta", product, new DateTime(2026, 1, 5, 14, 22, 0), null,
            description, amount, fee, "EUR", state, null));

    [Fact]
    public void La_pata_de_ahorros_no_se_importa_porque_ya_esta_en_la_cuenta_corriente()
    {
        var c = Classify(product: "Ahorros", description: "Al Pocket EUR Savings desde EUR", amount: 50m);

        Assert.Equal(MovementDisposition.Excluded, c.Disposition);
        Assert.Contains("traspaso ya apuntado", c.Reason);
    }

    [Fact]
    public void Un_movimiento_devuelto_no_entra()
    {
        var c = Classify(state: "DEVUELTO");

        Assert.Equal(MovementDisposition.Excluded, c.Disposition);
        Assert.Contains("devuelto", c.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Un_movimiento_pendiente_entra_como_prevision_no_como_hecho()
    {
        var c = Classify(state: "PENDIENTE");

        Assert.Equal(MovementDisposition.Import, c.Disposition);
        Assert.Equal(EntryStatus.Pending, c.Status);
    }

    [Fact]
    public void Un_movimiento_liquidado_entra_como_pagado()
    {
        var c = Classify(state: "COMPLETADO");

        Assert.Equal(MovementDisposition.Import, c.Disposition);
        Assert.Equal(EntryStatus.Paid, c.Status);
    }

    [Fact]
    public void El_signo_decide_la_direccion_y_el_importe_viaja_en_positivo()
    {
        var salida = Classify(amount: -12.35m);
        var entrada = Classify(amount: 2360.29m);

        Assert.Equal(EntryDirection.Out, salida.Direction);
        Assert.Equal(12.35m, salida.Amount);

        Assert.Equal(EntryDirection.In, entrada.Direction);
        Assert.Equal(2360.29m, entrada.Amount);
    }

    [Fact]
    public void Un_estado_desconocido_se_trata_como_liquidado_pero_deja_constancia()
    {
        var c = Classify(state: "LO_QUE_SEA");

        Assert.Equal(MovementDisposition.Import, c.Disposition);
        Assert.Equal(EntryStatus.Paid, c.Status);
        Assert.Contains("desconocido", c.Reason);
    }

    // ----------------------------------------------------------------------
    // Normalización de la descripción
    // ----------------------------------------------------------------------

    [Fact]
    public void Las_recargas_con_distintas_tarjetas_comparten_patron()
    {
        // Seis tarjetas distintas en el extracto real: si no colapsan, harían
        // falta seis reglas para lo que es el mismo movimiento.
        var a = RevolutMovementClassifier.Normalize("Una recarga de Apple Pay con *8693");
        var b = RevolutMovementClassifier.Normalize("Una recarga de Apple Pay con *6442");
        var c = RevolutMovementClassifier.Normalize("Una recarga de Apple Pay con *****");

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Equal("UNA RECARGA DE APPLE PAY CON", a);
    }

    [Fact]
    public void La_normalizacion_quita_tildes_y_unifica_espacios()
    {
        var n = RevolutMovementClassifier.Normalize("  Dinero  añadido a través de BIZUM ");

        Assert.Equal("DINERO ANADIDO A TRAVES DE BIZUM", n);
    }

    [Fact]
    public void Dos_nominas_de_meses_distintos_comparten_patron()
    {
        var a = RevolutMovementClassifier.Normalize("Pago de WOLTERS KLUWER TAX AND ACCOUNTING ESPANA S.L.");
        var b = RevolutMovementClassifier.Normalize("Pago de WOLTERS KLUWER TAX AND ACCOUNTING ESPAÑA S.L.");

        // La eñe del literal cambia entre meses en el extracto real.
        Assert.Equal(a, b);
    }
}
