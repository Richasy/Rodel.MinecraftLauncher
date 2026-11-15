namespace Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

/// <summary>Records actual request destinations while substituting only remote download responses.</summary>
internal sealed class SourceTransport(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    internal List<string> Addresses { get; } = [];

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Addresses)
            Addresses.Add(request.RequestUri!.AbsoluteUri);
        return Task.FromResult(respond(request));
    }
}