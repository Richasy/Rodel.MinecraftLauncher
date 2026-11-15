using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Serves synthetic game dependencies with explicit overlap and stalled-body barriers.</summary>
internal sealed class InstallationEndpoint : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly int _firstWaveSize;
    private readonly TaskCompletionSource _release = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _bodyStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _failureAllowed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource _bodyAborted = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ConcurrentDictionary<string, int> _requests = new(StringComparer.Ordinal);
    private int _active;
    private int _peak;

    private InstallationEndpoint(WebApplication app, int firstWaveSize)
    {
        _app = app;
        _firstWaveSize = firstWaveSize;
    }

    internal TaskCompletionSource FirstWave { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    internal bool FailLibrary { get; set; }
    internal int Peak => Volatile.Read(ref _peak);
    internal int Requests => _requests.Values.Sum();
    internal Task BodyAborted => _bodyAborted.Task;
    internal Uri Root { get; private set; } = null!;
    internal static byte[] Payload(string name) => name == "library1.jar"
        ? Encoding.UTF8.GetBytes(new string('x', 128 * 1024))
        : Encoding.UTF8.GetBytes("synthetic dependency: " + name);
    internal void Release() => _release.TrySetResult();
    internal void AllowFailure() => _failureAllowed.TrySetResult();

    internal static async Task<InstallationEndpoint> StartAsync(int firstWaveSize, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(options => options.Listen(IPAddress.Loopback, 0));
        var app = builder.Build();
        var endpoint = new InstallationEndpoint(app, firstWaveSize);
        app.MapGet("/{name}", endpoint.RespondAsync);
        try
        {
            await app.StartAsync(ct);
            endpoint.Root = new Uri(app.Urls.Single() + "/");
            return endpoint;
        }
        catch
        {
            await app.DisposeAsync();
            throw;
        }
    }

    internal JsonObject Metadata()
    {
        var libraries = new JsonArray();
        for (var i = 0; i < 4; i++)
        {
            libraries.Add(new JsonObject
            {
                ["name"] = $"fixture:library{i}:1",
                ["downloads"] = new JsonObject { ["artifact"] = Descriptor($"library{i}.jar", $"fixture/library{i}.jar") }
            });
        }

        libraries.Add(new JsonObject
        {
            ["name"] = "fixture:native:1",
            ["natives"] = new JsonObject { ["windows"] = "natives-windows" },
            ["downloads"] = new JsonObject
            {
                ["classifiers"] = new JsonObject { ["natives-windows"] = Descriptor("native.jar", "fixture/native.jar") }
            }
        });
        var logging = Descriptor("logging.xml");
        logging["id"] = "logging.xml";
        return new JsonObject
        {
            ["id"] = "installation-fixture",
            ["downloads"] = new JsonObject { ["client"] = Descriptor("client.jar") },
            ["libraries"] = libraries,
            ["logging"] = new JsonObject { ["client"] = new JsonObject { ["file"] = logging } }
        };
    }

    private JsonObject Descriptor(string name, string? path = null)
    {
        var bytes = Payload(name);
        var descriptor = new JsonObject
        {
            ["url"] = new Uri(Root, name).AbsoluteUri,
            ["size"] = bytes.Length,
            ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(bytes))
        };
        if (path is not null) descriptor["path"] = path;
        return descriptor;
    }

    private async Task RespondAsync(string name, HttpContext context)
    {
        _requests.AddOrUpdate(name, 1, (_, count) => count + 1);
        var active = Interlocked.Increment(ref _active);
        int previous;
        do { previous = Volatile.Read(ref _peak); }
        while (active > previous && Interlocked.CompareExchange(ref _peak, active, previous) != previous);
        if (active >= _firstWaveSize) FirstWave.TrySetResult();
        try
        {
            await _release.Task.WaitAsync(context.RequestAborted);
            if (FailLibrary && name == "library0.jar")
            {
                await _bodyStarted.Task.WaitAsync(context.RequestAborted);
                await _failureAllowed.Task.WaitAsync(context.RequestAborted);
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            var bytes = Payload(name);
            context.Response.ContentLength = bytes.Length;
            if (FailLibrary && name == "library1.jar")
            {
                await context.Response.Body.WriteAsync(bytes.AsMemory(0, bytes.Length - 1), context.RequestAborted);
                await context.Response.Body.FlushAsync(context.RequestAborted);
                _bodyStarted.TrySetResult();
                try { await Task.Delay(Timeout.InfiniteTimeSpan, context.RequestAborted); }
                finally { _bodyAborted.TrySetResult(); }

                return;
            }

            await context.Response.Body.WriteAsync(bytes, context.RequestAborted);
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
        finally { Interlocked.Decrement(ref _active); }
    }

    /// <summary>Stops only this fixture's server, aborting held requests before releasing its isolated storage.</summary>
    public async ValueTask DisposeAsync()
    {
        Release();
        AllowFailure();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await _app.StopAsync(deadline.Token); }
        finally { await _app.DisposeAsync(); }
    }
}