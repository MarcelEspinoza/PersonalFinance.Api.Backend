using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.ReopenMonth
{
    public record ReopenMonthCommand(Guid UserId, int Year, int Month) : IRequest<MonthlySummaryDto>;

    public class ReopenMonthCommandHandler : IRequestHandler<ReopenMonthCommand, MonthlySummaryDto>
    {
        private readonly IAppDbContext _db;
        private readonly MonthlySummaryLoader _loader;

        public ReopenMonthCommandHandler(IAppDbContext db, MonthlySummaryLoader loader)
        {
            _db = db;
            _loader = loader;
        }

        public async Task<MonthlySummaryDto> Handle(ReopenMonthCommand request, CancellationToken ct)
        {
            PeriodProvisioner.ValidateMonth(request.Year, request.Month);

            var period = await _db.MonthlyPeriods
                .FirstOrDefaultAsync(
                    p => p.UserId == request.UserId && p.Year == request.Year && p.Month == request.Month, ct)
                ?? throw new NotFoundException($"El periodo {request.Year}-{request.Month:D2} no existe.");

            if (period.Status != PeriodStatus.Closed)
                throw new BusinessRuleException("El periodo ya estÃ¡ abierto.");

            // Reabrir un mes invalida el arrastre del siguiente, asÃ­ que no se
            // permite si ese mes posterior ya estÃ¡ cerrado: habrÃ­a que
            // deshacer la cadena entera.
            var (nextYear, nextMonth) = PeriodProvisioner.NextMonth(request.Year, request.Month);

            var nextIsClosed = await _db.MonthlyPeriods.AnyAsync(
                p => p.UserId == request.UserId
                     && p.Year == nextYear
                     && p.Month == nextMonth
                     && p.Status == PeriodStatus.Closed, ct);

            if (nextIsClosed)
                throw new BusinessRuleException(
                    $"No se puede reabrir: el periodo {nextYear}-{nextMonth:D2} ya estÃ¡ cerrado. ReÃ¡brelo primero.");

            period.Status = PeriodStatus.Open;
            period.ClosingBalance = null;
            period.ClosedAt = null;

            await _db.SaveChangesAsync(ct);

            return await _loader.LoadAsync(request.UserId, period, ct);
        }
    }
}
