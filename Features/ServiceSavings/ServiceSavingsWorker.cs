namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>
    /// Runs the queued service reviews outside the HTTP request (the analysis can
    /// take minutes). Each job gets its own DI scope; a few jobs may run at once.
    /// Never lets an exception stop the host.
    /// </summary>
    public sealed class ServiceSavingsWorker : BackgroundService
    {
        private const int MaxParallelJobs = 2;

        private readonly ServiceSavingsQueue _queue;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ServiceSavingsWorker> _logger;

        public ServiceSavingsWorker(
            ServiceSavingsQueue queue,
            IServiceScopeFactory scopeFactory,
            ILogger<ServiceSavingsWorker> logger)
        {
            _queue = queue;
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var running = new List<Task>();
            try
            {
                await foreach (var reviewId in _queue.ReadAllAsync(stoppingToken))
                {
                    running.RemoveAll(task => task.IsCompleted);
                    while (running.Count >= MaxParallelJobs)
                    {
                        await Task.WhenAny(running);
                        running.RemoveAll(task => task.IsCompleted);
                    }

                    running.Add(RunJobAsync(reviewId, stoppingToken));
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service savings worker stopped unexpectedly.");
            }

            // Jobs never throw; wait for them so they can record their outcome.
            await Task.WhenAll(running);
        }

        private async Task RunJobAsync(Guid reviewId, CancellationToken stoppingToken)
        {
            await Task.Yield();
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IServiceSavingsService>();
                await service.RunReviewAsync(reviewId, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                await TryMarkFailedAsync(reviewId, ServiceSavingsMessages.Interrupted);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Service savings review {ReviewId} failed.", reviewId);
                await TryMarkFailedAsync(reviewId, ServiceSavingsMessages.DefaultError);
            }
        }

        private async Task TryMarkFailedAsync(Guid reviewId, string message)
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IServiceSavingsService>();
                await service.MarkFailedAsync(reviewId, message, timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not mark service savings review {ReviewId} as failed.", reviewId);
            }
        }
    }
}
