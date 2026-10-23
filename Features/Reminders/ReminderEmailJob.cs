using PersonalFinance.Api.Common.Interfaces;

namespace PersonalFinance.Api.Features.Reminders
{
    /// <summary>
    /// Every 30 minutes, emails the reminders that became due (from 08:00 Madrid time).
    /// Disabled with Reminders:EmailJobEnabled=false or when SendGrid has no API key.
    /// Never lets an exception stop the host.
    /// </summary>
    public sealed class ReminderEmailJob : BackgroundService
    {
        private static readonly TimeSpan Interval = TimeSpan.FromMinutes(30);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly IClock _clock;
        private readonly ILogger<ReminderEmailJob> _logger;

        public ReminderEmailJob(
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration,
            IClock clock,
            ILogger<ReminderEmailJob> logger)
        {
            _scopeFactory = scopeFactory;
            _configuration = configuration;
            _clock = clock;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_configuration.GetValue("Reminders:EmailJobEnabled", true))
            {
                _logger.LogInformation("Reminder email job disabled (Reminders:EmailJobEnabled=false).");
                return;
            }

            if (string.IsNullOrWhiteSpace(_configuration["SendGrid:ApiKey"]))
            {
                _logger.LogInformation("Reminder email job not started: SendGrid is not configured.");
                return;
            }

            try
            {
                using var timer = new PeriodicTimer(Interval);
                do
                {
                    await RunOnceAsync(stoppingToken);
                }
                while (await timer.WaitForNextTickAsync(stoppingToken));
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder email job stopped unexpectedly.");
            }
        }

        private async Task RunOnceAsync(CancellationToken ct)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dispatcher = scope.ServiceProvider.GetRequiredService<ReminderEmailDispatcher>();
                var sent = await dispatcher.SendDueAsync(_clock.UtcNow, ct);
                if (sent > 0) _logger.LogInformation("Reminder email job sent {Count} email(s).", sent);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Reminder email job run failed.");
            }
        }
    }
}
