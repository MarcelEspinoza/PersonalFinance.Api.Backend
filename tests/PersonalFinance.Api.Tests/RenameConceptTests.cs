using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Features.Ledger.Commands.CreateLedgerEntry;
using PersonalFinance.Api.Features.Ledger.Commands.RenameConcept;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

public class RenameConceptTests : LedgerTestBase
{
    private const string OldName = "Dinero de paso (no es mÃ­o)";
    private const string NewName = "Dinero de paso (no es mío)";

    [Fact]
    public async Task Renombrar_cambia_el_concepto_la_regla_y_lo_pendiente_pero_no_lo_pagado()
    {
        var (_, concept) = SeedConcept("Varios", OldName, ConceptKind.Expense);
        Db.RecurringRules.Add(new RecurringRule
        {
            UserId = UserId,
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            Frequency = RecurrenceFrequency.Monthly,
            DayOfMonth = 5,
            ForecastAmount = 10m,
            StartDate = new DateOnly(2030, 1, 1),
            Description = OldName
        });
        await Db.SaveChangesAsync();

        var paid = await Mediator.Send(new CreateLedgerEntryCommand(UserId, new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 2, 10),
            ForecastAmount = 20m,
            ActualAmount = 20m,
            Description = OldName
        }));
        var planned = await Mediator.Send(new CreateLedgerEntryCommand(UserId, new CreateLedgerEntryDto
        {
            ConceptId = concept.Id,
            Direction = EntryDirection.Out,
            DueDate = new DateOnly(2030, 2, 20),
            ForecastAmount = 30m,
            Description = OldName
        }));

        var result = await Mediator.Send(new RenameConceptCommand(UserId, concept.Id, "  " + NewName + "  "));

        Assert.Equal(NewName, result.Name);
        Db.ChangeTracker.Clear();
        Assert.Equal(NewName, (await Db.Concepts.SingleAsync(c => c.Id == concept.Id)).Name);
        Assert.Equal(NewName, (await Db.RecurringRules.SingleAsync(r => r.ConceptId == concept.Id)).Description);

        var entries = await Db.LedgerEntries.Where(e => e.ConceptId == concept.Id).ToListAsync();
        Assert.Equal(OldName, entries.Single(e => e.Id == paid.Id).Description);
        Assert.Equal(NewName, entries.Single(e => e.Id == planned.Id).Description);
        Assert.Contains(entries, e => e.RecurringRuleId != null);
        Assert.All(entries.Where(e => e.RecurringRuleId != null), e => Assert.Equal(NewName, e.Description));
    }

    [Fact]
    public async Task No_se_puede_renombrar_con_un_nombre_vacio_repetido_o_de_otro_usuario()
    {
        var (group, netflix) = SeedConcept("Suscripciones", "Netflix", ConceptKind.Expense);
        var spotify = new Concept { UserId = UserId, Name = "Spotify", Kind = ConceptKind.Expense, GroupId = group.Id };
        Db.Concepts.Add(spotify);
        await Db.SaveChangesAsync();

        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Mediator.Send(new RenameConceptCommand(UserId, spotify.Id, "netflix")));
        await Assert.ThrowsAsync<BusinessRuleException>(() =>
            Mediator.Send(new RenameConceptCommand(UserId, spotify.Id, "   ")));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            Mediator.Send(new RenameConceptCommand(Guid.NewGuid(), netflix.Id, "Otro nombre")));

        var renamed = await Mediator.Send(new RenameConceptCommand(UserId, netflix.Id, "NETFLIX"));
        Assert.Equal("NETFLIX", renamed.Name);
    }
}
