using PersonalFinance.Api.Features.Settlements;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;
using PersonalFinance.Domain.Settlements;
using PersonalFinance.Domain.Settlements.Entities;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Cuentas compartidas con mamá y Vane: que los totales cuadren, que el
/// reparto funcione y que el mensaje salga con el formato de siempre.
/// </summary>
public class SettlementTests : LedgerTestBase
{
    private SettlementService CreateService() => new(Db, Clock);

    private Counterparty SeedPerson(string name, string? phone = null)
    {
        var person = new Counterparty { UserId = UserId, Name = name, PhoneNumber = phone };
        Db.Counterparties.Add(person);
        Db.SaveChanges();
        return person;
    }

    private async Task<SettlementDetailDto> SeedSettlementAsync(Counterparty person)
    {
        var service = CreateService();
        return await service.CreateAsync(
            UserId,
            new CreateSettlementDto(
                person.Id,
                "CUENTAS SEPTIEMBRE 2026",
                new DateOnly(2026, 9, 1),
                new DateOnly(2026, 9, 30),
                null,
                "Gracias 😊"),
            CancellationToken.None);
    }

    [Fact]
    public async Task El_total_pendiente_resta_deducciones_y_pagos()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var settlement = await SeedSettlementAsync(person);

        await service.AddLineAsync(UserId, settlement.Id,
            new SaveLineDto("Charge", "Casa septiembre", 450m, null, null), CancellationToken.None);
        await service.AddLineAsync(UserId, settlement.Id,
            new SaveLineDto("Deduction", "Disney+", 3.50m, null, null), CancellationToken.None);
        var result = await service.AddLineAsync(UserId, settlement.Id,
            new SaveLineDto("Payment", "Bizum 12/09", 200m, null, null), CancellationToken.None);

        Assert.Equal(450m, result.Totals.Charges);
        Assert.Equal(3.50m, result.Totals.Deductions);
        Assert.Equal(200m, result.Totals.Payments);
        Assert.Equal(246.50m, result.Totals.Pending);
    }

    [Fact]
    public async Task El_pendiente_anterior_se_arrastra_al_total()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var settlement = await SeedSettlementAsync(person);

        await service.AddLineAsync(UserId, settlement.Id,
            new SaveLineDto("Charge", "Pasanaco", 200m, null, null), CancellationToken.None);
        var result = await service.UpdateAsync(UserId, settlement.Id,
            new UpdateSettlementDto(null, null, null, 434m, null), CancellationToken.None);

        Assert.Equal(634m, result.Totals.Pending);
        Assert.Contains("Pendiente anterior: 434,00 €", result.Message);
    }

    [Fact]
    public async Task Un_gasto_a_medias_guarda_el_importe_total()
    {
        var service = CreateService();
        var person = SeedPerson("Vane");
        var settlement = await SeedSettlementAsync(person);
        var entry = SeedEntry("Copagos", 102m, new DateOnly(2026, 9, 10));

        var result = await service.AddFromEntryAsync(UserId, settlement.Id,
            new AddFromEntryDto(entry.Id, null, 50m, null), CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Equal(51m, line.Amount);
        Assert.Equal(102m, line.FullAmount);
        Assert.Contains("Copagos: 51,00 € (102,00 € total)", result.Message);
    }

    [Fact]
    public async Task Un_gasto_entero_no_muestra_el_total_repetido()
    {
        var service = CreateService();
        var person = SeedPerson("Vane");
        var settlement = await SeedSettlementAsync(person);
        var entry = SeedEntry("Caser", 14.12m, new DateOnly(2026, 9, 3));

        var result = await service.AddFromEntryAsync(UserId, settlement.Id,
            new AddFromEntryDto(entry.Id, null, null, null), CancellationToken.None);

        var line = Assert.Single(result.Lines);
        Assert.Null(line.FullAmount);
        Assert.Contains("Caser: 14,12 €", result.Message);
        Assert.DoesNotContain("total)", result.Message);
    }

    [Fact]
    public async Task Un_ingreso_se_imputa_como_pago_recibido()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var settlement = await SeedSettlementAsync(person);
        var entry = SeedEntry("Bizum mamá", 100m, new DateOnly(2026, 9, 5), EntryDirection.In);

        var result = await service.AddFromEntryAsync(UserId, settlement.Id,
            new AddFromEntryDto(entry.Id, null, null, null), CancellationToken.None);

        Assert.Equal("Payment", Assert.Single(result.Lines).Kind);
        Assert.Equal(-100m, result.Totals.Pending);
    }

    [Fact]
    public async Task No_se_puede_imputar_dos_veces_el_mismo_movimiento()
    {
        var service = CreateService();
        var person = SeedPerson("Vane");
        var settlement = await SeedSettlementAsync(person);
        var entry = SeedEntry("Caser", 14.12m, new DateOnly(2026, 9, 3));

        await service.AddFromEntryAsync(UserId, settlement.Id,
            new AddFromEntryDto(entry.Id, null, null, null), CancellationToken.None);

        await Assert.ThrowsAsync<PersonalFinance.Api.Common.Exceptions.BusinessRuleException>(() =>
            service.AddFromEntryAsync(UserId, settlement.Id,
                new AddFromEntryDto(entry.Id, null, null, null), CancellationToken.None));
    }

    [Fact]
    public async Task Los_candidatos_solo_traen_los_movimientos_del_periodo()
    {
        var service = CreateService();
        var person = SeedPerson("Vane");
        var settlement = await SeedSettlementAsync(person);

        var dentro = SeedEntry("Dentro", 10m, new DateOnly(2026, 9, 15));
        SeedEntry("Fuera", 20m, new DateOnly(2026, 10, 15));

        var candidates = await service.GetCandidatesAsync(UserId, settlement.Id, CancellationToken.None);

        Assert.Equal(dentro.Id, Assert.Single(candidates).LedgerEntryId);
    }

    [Fact]
    public async Task Asignar_un_movimiento_abre_la_liquidacion_si_no_existe()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var entry = SeedEntry("Casa", 350m, Clock.Today);

        var result = await service.AssignEntryAsync(UserId,
            new AssignEntryDto(person.Id, entry.Id, null, null, null), CancellationToken.None);

        Assert.Equal("Draft", result.Status);
        Assert.Equal(350m, result.Totals.Pending);
        Assert.Equal(new DateOnly(2026, 1, 1), result.PeriodStart);
        Assert.Equal(new DateOnly(2026, 1, 31), result.PeriodEnd);
    }

    [Fact]
    public async Task Asignar_dos_veces_reutiliza_el_mismo_borrador()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var primera = SeedEntry("Casa", 350m, Clock.Today);
        var segunda = SeedEntry("Pasanaco", 200m, Clock.Today);

        var uno = await service.AssignEntryAsync(UserId,
            new AssignEntryDto(person.Id, primera.Id, null, null, null), CancellationToken.None);
        var dos = await service.AssignEntryAsync(UserId,
            new AssignEntryDto(person.Id, segunda.Id, null, null, null), CancellationToken.None);

        Assert.Equal(uno.Id, dos.Id);
        Assert.Equal(2, dos.Lines.Count);
    }

    [Fact]
    public async Task Una_liquidacion_enviada_no_se_puede_editar_hasta_reabrirla()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");
        var settlement = await SeedSettlementAsync(person);

        var sent = await service.MarkSentAsync(UserId, settlement.Id, CancellationToken.None);
        Assert.Equal("Sent", sent.Status);
        Assert.NotNull(sent.SentAt);

        await Assert.ThrowsAsync<PersonalFinance.Api.Common.Exceptions.BusinessRuleException>(() =>
            service.AddLineAsync(UserId, settlement.Id,
                new SaveLineDto("Charge", "Tarde", 10m, null, null), CancellationToken.None));

        var reopened = await service.ReopenAsync(UserId, settlement.Id, CancellationToken.None);
        Assert.Equal("Draft", reopened.Status);
        Assert.Null(reopened.SentAt);
    }

    [Fact]
    public async Task El_enlace_de_whatsapp_necesita_telefono()
    {
        var service = CreateService();
        var sinTelefono = await SeedSettlementAsync(SeedPerson("Sin teléfono"));
        Assert.Null(sinTelefono.WhatsAppUrl);

        var conTelefono = await SeedSettlementAsync(SeedPerson("Con teléfono", "+34 600 11 22 33"));
        Assert.NotNull(conTelefono.WhatsAppUrl);
        Assert.StartsWith("https://wa.me/34600112233?text=", conTelefono.WhatsAppUrl);
    }

    [Fact]
    public async Task El_telefono_se_guarda_solo_con_digitos()
    {
        var service = CreateService();
        var person = SeedPerson("Mamá");

        var saved = await service.SavePersonAsync(UserId, person.Id,
            new SavePersonDto("Mamá", "+34 600-11 22 33"), CancellationToken.None);

        Assert.Equal("34600112233", saved.PhoneNumber);
    }

    [Fact]
    public void El_mensaje_reproduce_el_formato_de_siempre()
    {
        var settlement = new Settlement
        {
            Title = "CUENTAS AGOSTO/SEPTIEMBRE 2026",
            ClosingNote = "Si hay algo que no se entienda o hay dudas, me avisas.",
            Lines =
            {
                new SettlementLine { Kind = SettlementLineKind.Charge, Description = "Casa agosto", Amount = 350m, SortOrder = 0 },
                new SettlementLine { Kind = SettlementLineKind.Charge, Description = "Casa septiembre", Amount = 450m, SortOrder = 1 },
                new SettlementLine { Kind = SettlementLineKind.Payment, Description = "Bizum 03/09", Amount = 766m, SortOrder = 2000 }
            }
        };

        var message = SettlementMessageBuilder.Build(settlement);

        Assert.Contains("CUENTAS AGOSTO/SEPTIEMBRE 2026", message);
        Assert.Contains("GASTOS", message);
        Assert.Contains("• Casa agosto: 350,00 €", message);
        Assert.Contains("TOTAL GASTOS: 800,00 €", message);
        Assert.Contains("TOTAL PAGADO: 766,00 €", message);
        Assert.Contains("TOTAL PENDIENTE ➡️ 34,00 €", message);
        Assert.EndsWith("Si hay algo que no se entienda o hay dudas, me avisas.", message);
    }

    [Fact]
    public void Un_saldo_a_favor_se_anuncia_sin_signo_negativo()
    {
        var settlement = new Settlement
        {
            Title = "CUENTAS",
            Lines =
            {
                new SettlementLine { Kind = SettlementLineKind.Charge, Description = "Gasto", Amount = 50m },
                new SettlementLine { Kind = SettlementLineKind.Payment, Description = "Pago", Amount = 80m, SortOrder = 2000 }
            }
        };

        var message = SettlementMessageBuilder.Build(settlement);

        Assert.Contains("TOTAL PENDIENTE (a tu favor) 30,00 €", message);
        Assert.DoesNotContain("-30,00", message);
    }

    private LedgerEntry SeedEntry(
        string description,
        decimal amount,
        DateOnly date,
        EntryDirection direction = EntryDirection.Out)
    {
        var kind = direction == EntryDirection.In ? ConceptKind.Income : ConceptKind.Expense;
        var (_, concept) = SeedConcept($"Grupo {description}", $"Concepto {description}", kind);

        var period = Db.MonthlyPeriods.FirstOrDefault(p =>
            p.UserId == UserId && p.Year == date.Year && p.Month == date.Month);

        if (period is null)
        {
            period = new MonthlyPeriod { UserId = UserId, Year = date.Year, Month = date.Month };
            Db.MonthlyPeriods.Add(period);
            Db.SaveChanges();
        }

        var entry = new LedgerEntry
        {
            UserId = UserId,
            PeriodId = period.Id,
            ConceptId = concept.Id,
            Direction = direction,
            Status = EntryStatus.Paid,
            DueDate = date,
            ValueDate = date,
            ForecastAmount = amount,
            ActualAmount = amount,
            Description = description
        };

        Db.LedgerEntries.Add(entry);
        Db.SaveChanges();

        return entry;
    }
}
