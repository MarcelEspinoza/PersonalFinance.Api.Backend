using MediatR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Data;
using PersonalFinance.Api.Features.Ledger;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Tests;

/// <summary>Reloj fijo: sin él, los tests cambiarían de resultado con el calendario.</summary>
public sealed class TestClock : IClock
{
    public DateOnly Today { get; set; } = new(2026, 1, 15);

    public DateTime UtcNow => Today.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
}

/// <summary>Captura los descuadres de cierre para poder afirmarlos en los tests.</summary>
public sealed class RecordingAuditLog : ILedgerAuditLog
{
    public List<(int Year, int Month, decimal Declared, decimal Computed)> Mismatches { get; } = new();

    public void ClosingBalanceMismatch(Guid userId, int year, int month, decimal declared, decimal computed) =>
        Mismatches.Add((year, month, declared, computed));
}

/// <summary>
/// Base con una SQLite en memoria por test y el contenedor real de la
/// aplicación: así los tests también comprueban que el registro de MediatR
/// y las dependencias están bien montados.
/// </summary>
public abstract class LedgerTestBase : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly ServiceProvider _provider;

    protected readonly AppDbContext Db;
    protected readonly IMediator Mediator;
    protected readonly TestClock Clock = new();
    protected readonly RecordingAuditLog Audit = new();
    protected readonly Guid UserId = Guid.NewGuid();

    protected LedgerTestBase()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        Db = new AppDbContext(options);
        Db.Database.EnsureCreated();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLedgerModule();

        // Se comparte la misma instancia que usan los tests para sembrar, y se
        // sustituyen reloj y log por los dobles. Al registrarse después, estas
        // resoluciones ganan a las del módulo.
        services.AddSingleton(Db);
        services.AddSingleton<IAppDbContext>(Db);
        services.AddSingleton<IClock>(Clock);
        services.AddSingleton<ILedgerAuditLog>(Audit);

        _provider = services.BuildServiceProvider();
        Mediator = _provider.GetRequiredService<IMediator>();
    }

    protected (ConceptGroup Group, Concept Concept) SeedConcept(
        string groupName, string conceptName, ConceptKind kind)
    {
        var group = new ConceptGroup { UserId = UserId, Name = groupName, Kind = kind };
        var concept = new Concept { UserId = UserId, Name = conceptName, Kind = kind, GroupId = group.Id };

        Db.ConceptGroups.Add(group);
        Db.Concepts.Add(concept);
        Db.SaveChanges();

        return (group, concept);
    }

    public void Dispose()
    {
        _provider.Dispose();
        Db.Dispose();
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }
}
