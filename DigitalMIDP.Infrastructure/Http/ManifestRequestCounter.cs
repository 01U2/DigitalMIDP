namespace DigitalMIDP.Infrastructure.Http;

internal sealed class ManifestRequestCounter : IDisposable
{
    private static readonly AsyncLocal<ManifestRequestCounter?> Current = new();
    private readonly ManifestRequestCounter? previous = Current.Value;
    private int count;

    public ManifestRequestCounter() => Current.Value = this;
    public int Count => Volatile.Read(ref count);
    public void Dispose() => Current.Value = previous;

    internal sealed class Handler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (Current.Value is { } scope) Interlocked.Increment(ref scope.count);
            return base.SendAsync(request, ct);
        }
    }
}