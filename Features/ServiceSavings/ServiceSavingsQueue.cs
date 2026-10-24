using System.Threading.Channels;

namespace PersonalFinance.Api.Features.ServiceSavings
{
    /// <summary>
    /// In-memory queue of service reviews waiting to run. Reviews queued before a
    /// restart are lost; they are marked failed once they become stale.
    /// </summary>
    public sealed class ServiceSavingsQueue
    {
        private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
            new UnboundedChannelOptions { SingleReader = true });

        public bool Enqueue(Guid reviewId) => _channel.Writer.TryWrite(reviewId);

        public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
    }
}
