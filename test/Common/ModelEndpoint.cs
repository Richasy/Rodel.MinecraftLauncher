using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Tests;

/// <summary>A real ephemeral HTTP boundary with substituted model responses; it never contacts a public provider.</summary>
internal sealed class ModelEndpoint(WebApplication app, Uri endpoint) : IAsyncDisposable
{
    /// <summary>Gets the actual listening address after Kestrel has completed startup.</summary>
    public Uri Endpoint { get; } = endpoint;

    /// <summary>Starts one controlled model endpoint without changing process environment or shared ports.</summary>
    public static async Task<ModelEndpoint> StartAsync(Func<HttpContext, Task> respond, CancellationToken ct, string[]? methods = null, bool tokenApis = false)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var application = builder.Build();
        application.MapMethods("/{**path}", methods ?? ["POST"], (HttpContext context, string? path) =>
        {
            path ??= string.Empty;
            if (!tokenApis && (path.EndsWith("/input_tokens", StringComparison.Ordinal)
                || path.EndsWith("/count_tokens", StringComparison.Ordinal) || path.EndsWith(":countTokens", StringComparison.Ordinal)))
            {
                context.Response.StatusCode = 404;
                return Task.CompletedTask;
            }

            return respond(context);
        });
        try
        {
            await application.StartAsync(ct);
            return new ModelEndpoint(application, new Uri(application.Urls.Single() + "/"));
        }
        catch
        {
            await application.DisposeAsync();
            throw;
        }
    }

    /// <summary>Reads the real request JSON so cases can inspect protocol/continuation meaning independently of provider output.</summary>
    public static async Task<JsonObject> ReadRequestAsync(HttpContext context)
    {
        using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
        return JsonNode.Parse(await reader.ReadToEndAsync(context.RequestAborted))!.AsObject();
    }

    /// <summary>Writes fixed wire fixtures in byte fragments, including inside multibyte UTF-8 scalars.</summary>
    public static async Task SseAsync(HttpContext context, string[] events)
    {
        if (!context.Response.HasStarted) context.Response.ContentType = "text/event-stream";
        var bytes = Encoding.UTF8.GetBytes(string.Concat(events.Select(value => "data: " + value + "\n\n")));
        for (var offset = 0; offset < bytes.Length; offset += 7)
        {
            await context.Response.Body.WriteAsync(bytes.AsMemory(offset, Math.Min(7, bytes.Length - offset)), context.RequestAborted);
            await context.Response.Body.FlushAsync(context.RequestAborted);
        }
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await app.StopAsync(deadline.Token); }
        finally { await app.DisposeAsync(); }
    }
}