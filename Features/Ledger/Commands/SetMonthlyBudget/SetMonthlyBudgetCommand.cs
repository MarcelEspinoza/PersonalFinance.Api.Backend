using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Domain.Ledger.Entities;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.SetMonthlyBudget
{
    /// <summary>
    /// Fija el límite de un gasto variable sólo para un mes, por encima de su
    /// límite habitual. Bajarlo a lo ya gastado deja su reserva estimada en 0.
    /// Devuelve el id del presupuesto del mes.
    /// </summary>
    public record SetMonthlyBudgetCommand(Guid UserId, Guid ConceptId, int Year, int Month, decimal LimitAmount)
        : IRequest<Guid>;

    public class SetMonthlyBudgetCommandHandler : IRequestHandler<SetMonthlyBudgetCommand, Guid>
    {
        private readonly IAppDbContext _db;

        public SetMonthlyBudgetCommandHandler(IAppDbContext db) => _db = db;

        public async Task<Guid> Handle(SetMonthlyBudgetCommand request, CancellationToken ct)
        {
            PeriodProvisioner.ValidateMonth(request.Year, request.Month);

            if (request.LimitAmount < 0m)
                throw new BusinessRuleException("El límite mensual no puede ser negativo.");

            var concept = await LedgerGuards.EnsureConceptBelongsToUserAsync(_db, request.UserId, request.ConceptId, ct);
            if (!concept.IsActive || concept.Kind != ConceptKind.Expense || concept.Nature != ConceptNature.Variable)
                throw new BusinessRuleException(
                    $"«{concept.Name}» no es un gasto variable con presupuesto: no tiene reserva que ajustar.");

            var closed = await _db.MonthlyPeriods.AnyAsync(period =>
                period.UserId == request.UserId &&
                period.Year == request.Year &&
                period.Month == request.Month &&
                period.Status == PeriodStatus.Closed, ct);
            if (closed)
                throw new BusinessRuleException(
                    $"El periodo {request.Year}-{request.Month:D2} está cerrado: no se puede cambiar su presupuesto.");

            var budget = await _db.MonthlyBudgets.FirstOrDefaultAsync(item =>
                item.UserId == request.UserId &&
                item.ConceptId == concept.Id &&
                item.Year == request.Year &&
                item.Month == request.Month, ct);
            if (budget is null)
            {
                budget = new MonthlyBudget
                {
                    UserId = request.UserId,
                    ConceptId = concept.Id,
                    Year = request.Year,
                    Month = request.Month
                };
                _db.MonthlyBudgets.Add(budget);
            }

            budget.LimitAmount = Math.Round(request.LimitAmount, 2);
            await _db.SaveChangesAsync(ct);

            return budget.Id;
        }
    }
}
