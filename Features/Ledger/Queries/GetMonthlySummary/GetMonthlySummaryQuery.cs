using MediatR;
using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Features.Ledger.Common;
using PersonalFinance.Api.Features.Ledger.Dtos;

namespace PersonalFinance.Api.Features.Ledger.Queries.GetMonthlySummary
{
    /// <summary>
    /// Cuadro del mes. El UserId lo pone la capa de API desde el token: nunca
    /// llega en el cuerpo ni en la query string.
    /// </summary>
    public record GetMonthlySummaryQuery(Guid UserId, int Year, int Month) : IRequest<MonthlySummaryDto>;

    public class GetMonthlySummaryQueryHandler : IRequestHandler<GetMonthlySummaryQuery, MonthlySummaryDto>
    {
        private readonly PeriodProvisioner _provisioner;
        private readonly MonthlySummaryLoader _loader;

        public GetMonthlySummaryQueryHandler(PeriodProvisioner provisioner, MonthlySummaryLoader loader)
        {
            _provisioner = provisioner;
            _loader = loader;
        }

        public async Task<MonthlySummaryDto> Handle(GetMonthlySummaryQuery request, CancellationToken ct)
        {
            var period = await _provisioner.GetOrOpenAsync(request.UserId, request.Year, request.Month, ct);

            return await _loader.LoadAsync(request.UserId, period, ct);
        }
    }
}
