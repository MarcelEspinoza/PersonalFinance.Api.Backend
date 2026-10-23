using AutoMapper;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Api.MappingProfiles;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Models;
using PersonalFinance.Api.Models.Dtos;
using PersonalFinance.Api.Models.Entities;
using PersonalFinance.Api.Models.Enums;
using PersonalFinance.Api.Services;

namespace PersonalFinance.Api.Tests;

/// <summary>
/// Los préstamos de otro usuario no se pueden leer, modificar ni borrar, y un
/// préstamo nuevo no puede apuntar a categorías privadas o pasanacos ajenos.
/// </summary>
public sealed class LoanOwnershipTests : LedgerTestBase
{
    private readonly Guid _otherUserId = Guid.NewGuid();

    [Fact]
    public async Task Otro_usuario_no_puede_leer_modificar_ni_borrar_un_prestamo_ajeno()
    {
        var service = CreateService();
        var loan = SeedLoan("Coche");

        Assert.Null(await service.GetLoanAsync(loan.Id, _otherUserId));
        Assert.Empty(await service.GetLoansAsync(_otherUserId));
        await Assert.ThrowsAsync<NotFoundException>(() =>
            service.UpdateLoanAsync(loan.Id, _otherUserId, NewLoanDto("Cambiado", _otherUserId)));
        await service.DeleteLoanAsync(loan.Id, _otherUserId);

        Db.ChangeTracker.Clear();
        var stored = await Db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal("Coche", stored.Name);
    }

    [Fact]
    public async Task El_propietario_puede_modificar_y_borrar_su_prestamo_sin_cambiar_de_dueno()
    {
        var service = CreateService();
        var loan = SeedLoan("Coche");
        Assert.NotNull(await service.GetLoanAsync(loan.Id, UserId));

        var dto = NewLoanDto("Coche nuevo", _otherUserId);
        dto.Id = loan.Id;
        await service.UpdateLoanAsync(loan.Id, UserId, dto);

        Db.ChangeTracker.Clear();
        var updated = await Db.Loans.SingleAsync(l => l.Id == loan.Id);
        Assert.Equal("Coche nuevo", updated.Name);
        Assert.Equal(UserId, updated.UserId);

        await service.DeleteLoanAsync(loan.Id, UserId);
        Assert.False(await Db.Loans.AnyAsync());
    }

    [Fact]
    public async Task No_se_puede_crear_un_prestamo_con_un_pasanaco_o_una_categoria_ajenos()
    {
        var service = CreateService();
        var pasanaco = new Pasanaco
        {
            UserId = UserId,
            Name = "Familia",
            MonthlyAmount = 100m,
            TotalParticipants = 5,
            CurrentRound = 1,
            StartMonth = 1,
            StartYear = 2026
        };
        var privateCategory = new Category { Name = "Privada", UserId = UserId };
        Db.Pasanacos.Add(pasanaco);
        Db.Categories.Add(privateCategory);
        await Db.SaveChangesAsync();

        var withPasanaco = NewLoanDto("Préstamo", _otherUserId);
        withPasanaco.PasanacoId = pasanaco.Id;
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateLoanAsync(withPasanaco));

        var withCategory = NewLoanDto("Préstamo", _otherUserId);
        withCategory.CategoryId = privateCategory.Id;
        await Assert.ThrowsAsync<NotFoundException>(() => service.CreateLoanAsync(withCategory));

        Assert.False(await Db.Loans.AnyAsync());
    }

    private LoanService CreateService() =>
        new(Db, new MapperConfiguration(cfg => cfg.AddProfile<MappingProfile>()).CreateMapper());

    private Loan SeedLoan(string name)
    {
        var loan = new Loan
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            Type = LoanType.Bank,
            Name = name,
            PrincipalAmount = 1000m,
            OutstandingAmount = 1000m,
            StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            CategoryId = DefaultCategories.BankLoan
        };
        Db.Loans.Add(loan);
        Db.SaveChanges();
        return loan;
    }

    private static LoanDto NewLoanDto(string name, Guid userId) => new()
    {
        UserId = userId,
        Type = LoanType.Bank,
        Name = name,
        PrincipalAmount = 1000m,
        OutstandingAmount = 1000m,
        StartDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        CategoryId = DefaultCategories.BankLoan
    };
}
