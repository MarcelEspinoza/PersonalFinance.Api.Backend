using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Exceptions;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;
using PersonalFinance.Domain.Ledger.Calculations;
using PersonalFinance.Domain.Ledger.Enums;

namespace PersonalFinance.Api.Features.Ledger.Commands.CloseMonth
{
    public record CloseMonthCommand(Guid UserId, int Year, int Month, CloseMonthDto Payload)
        : IRequest<MonthlySummaryDto>;

    public class CloseMonthCommandHandler : IRequestHandler<CloseMonthCommand, MonthlySummaryDto>
    {
        private readonly IAppDbContext _db;
        private readonly IClock _clock;
        private readonly MonthlySummaryLoader _loader;
        private readonly ILedgerAuditLog _audit;

        public CloseMonthCommandHandler(
            IAppDbContext db, IClock clock, MonthlySummaryLoader loader, ILedgerAuditLog audit)
        {
            _db = db;
            _clock = clock;
            _loader = loader;
            _audit = audit;
        }

        public async Task<MonthlySummaryDto> Handle(CloseMonthCommand request, CancellationToken ct)
        {
            PeriodProvisioner.ValidateMonth(request.Year, request.Month);

            var period = await _db.MonthlyPeriods
                .FirstOrDefaultAsync(
                    p => p.UserId == request.UserId && p.Year == request.Year && p.Month == request.Month, ct)
                ?? throw new NotFoundException($"El periodo {request.Year}-{request.Month:D2} no existe.");

            var entries = await _db.LedgerEntries
                .Where(e => e.UserId == request.UserId && e.PeriodId == period.Id)
                .ToListAsync(ct);

            var computed = LedgerTotalsCalculator.ComputeClosingBalance(period.CarryOverAmount, entries);
            var declared = request.Payload?.ActualClosingBalance;

            period.ClosingBalance = declared ?? computed;
            period.Status = PeriodStatus.Closed;
            period.ClosedAt = _clock.UtcNow;

            if (declared.HasValue && declared.Value != computed)
            {
                _audit.ClosingBalanceMismatch(
                    request.UserId, request.Year, request.Month, declared.Value, computed);
            }

            // El cierre alimenta el arrastre del mes siguiente si ya estaba abierto.
            var (nextYear, nextMonth) = PeriodProvisioner.NextMonth(request.Year, request.Month);

            var next = await _db.MonthlyPeriods
                .FirstOrDefaultAsync(p => p.UserId == request.UserId && p.Year == nextYear && p.Month == nextMonth, ct);

            if (next is not null && next.Status != PeriodStatus.Closed)
                next.CarryOverAmount = period.ClosingBalance.Value;

            await _db.SaveChangesAsync(ct);

            return await _loader.LoadAsync(request.UserId, period, ct);
        }
    }
}
