using Microsoft.EntityFrameworkCore;
using PersonalFinance.Api.Data;

namespace PersonalFinance.Api.Features.Calendar
{
    public sealed class GoogleCalendarWorker(
        IServiceScopeFactory scopes, ILogger<GoogleCalendarWorker> logger) : BackgroundService
    {
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = scopes.CreateScope();
                    if (scope.ServiceProvider.GetRequiredService<GoogleCalendarSettings>().IsConfigured)
                    {
                        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                        var users = await db.GoogleCalendarConnections.AsNoTracking()
                            .Where(item => item.RefreshToken != null && !item.NeedsReconnect)
                            .Select(item => item.UserId).ToListAsync(stoppingToken);
                        foreach (var userId in users)
                        {
                            using var userScope = scopes.CreateScope();
                            await userScope.ServiceProvider.GetRequiredService<GoogleCalendarService>()
                                .SynchronizeAsync(userId, stoppingToken);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Google Calendar worker could not process pending synchronization.");
                }
                await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);
            }
        }
    }
}
