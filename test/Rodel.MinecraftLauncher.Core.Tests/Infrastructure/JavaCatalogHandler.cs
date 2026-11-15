using System.Net;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Returns a controlled catalog response through the service's HTTP seam without using a public server.</summary>
internal sealed class JavaCatalogHandler(HttpStatusCode status, string catalog) : HttpMessageHandler
{
    internal int Requests { get; private set; }

    /// <inheritdoc/>
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Requests++;
        return Task.FromResult(new HttpResponseMessage(status)
        {
            RequestMessage = request,
            Content = new StringContent(catalog, Encoding.UTF8, "application/json")
        });
    }
}