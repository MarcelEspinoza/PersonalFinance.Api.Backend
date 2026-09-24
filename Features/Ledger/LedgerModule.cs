using PersonalFinance.Api.Common.Interfaces;
using PersonalFinance.Api.Common.Services;
using PersonalFinance.Api.Features.Ledger.Common;

namespace PersonalFinance.Api.Features.Ledger
{
    /// <summary>
    /// Registro de la rodaja del libro mayor: MediatR descubre los handlers
    /// por ensamblado, y aquí sólo quedan las piezas compartidas.
    /// </summary>
    public static class LedgerModule
    {
        public static IServiceCollection AddLedgerModule(this IServiceCollection services)
        {
            services.AddMediatR(cfg => cfg.RegisterServicesFromAssemblyContaining<PeriodProvisioner>());

            services.AddSingleton<IClock, SystemClock>();
            services.AddScoped<ILedgerAuditLog, LedgerAuditLog>();
            services.AddScoped<PeriodProvisioner>();
            services.AddScoped<MonthlySummaryLoader>();

            return services;
        }
    }
}
