// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.ComponentModel;
using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Discovers and controls an explicitly selected running App without reading its package preference files.</summary>
public sealed class AppControlService
{
    private readonly LauncherPaths _paths;
    private readonly string _directory;
    internal static string RegistryDirectory(LauncherPaths paths) => Path.Combine(paths.Root, "app", "hosts");

    /// <summary>Creates a controller scoped to the selected launcher data root.</summary>
    public AppControlService(LauncherPaths paths) : this(paths, RegistryDirectory(paths)) { }
    internal AppControlService(LauncherPaths paths, string directory) { _paths = paths; _directory = directory; }

    /// <summary>Lists live matching App processes with stable IDs; stale or malformed records remain explicit warnings.</summary>
    public AppControlList List()
    {
        var items = new List<AppControlInstance>();
        var warnings = new List<AppControlDiscoveryWarning>();
        foreach (var host in Registrations(warnings))
        {
            items.Add(new()
            {
                Id = host.Id,
                ProcessId = host.ProcessId,
                StartedTicks = host.StartedTicks,
                DataRoot = host.DataRoot,
                PackageIdentity = host.PackageIdentity
            });
        }

        return new() { Items = [.. items], Warnings = [.. warnings] };
    }

    private IEnumerable<AppControlRegistration> Registrations(List<AppControlDiscoveryWarning> warnings)
    {
        if (!Directory.Exists(_directory)) yield break;
        foreach (var file in Directory.EnumerateFiles(_directory, "*.json"))
        {
            AppControlRegistration host;
            try
            {
                host = JsonFiles.ReadObject(file).Deserialize(AppControlWireJsonContext.Default.AppControlRegistration)
                    ?? throw new InvalidDataException("The App discovery record is empty.");
                if (!Guid.TryParseExact(host.Id, "N", out _) || Path.GetFileNameWithoutExtension(file) != host.Id
                    || host.PipeName != "rodel-app-" + host.Id || host.Nonce?.Length != 64)
                {
                    throw new InvalidDataException("The App discovery identity is invalid.");
                }

                using var process = Process.GetProcessById(host.ProcessId);
                if (process.StartTime.ToUniversalTime().Ticks != host.StartedTicks)
                    throw new InvalidDataException("The App process identity has changed.");
                if (AppControlProcessIdentity.PackageFor(process) != host.PackageIdentity)
                    throw new InvalidDataException("The App's actual OS package identity does not match its discovery record.");
                if (!string.Equals(ResourceLease.CanonicalPath(_paths.Root), host.DataRoot, StringComparison.OrdinalIgnoreCase)) continue;
            }
            catch (Exception error) when (error is IOException or InvalidDataException or ArgumentException or InvalidOperationException or JsonException or Win32Exception)
            {
                warnings.Add(new() { Record = Path.GetFileName(file), Message = error.Message });
                continue;
            }

            yield return host;
        }
    }

    /// <summary>Executes one bounded semantic operation; disconnect or timeout never replays an App mutation.</summary>
    public async Task<JsonObject> InvokeAsync(string id, AppControlRequest request, CancellationToken ct = default)
    {
        var warnings = new List<AppControlDiscoveryWarning>();
        var host = Registrations(warnings).SingleOrDefault(item => item.Id == id)
            ?? throw new LauncherOperationException("APP_NOT_RUNNING", "No live selected App uses this data root.",
                new JsonObject { ["warnings"] = JsonSerializer.SerializeToNode([.. warnings], AppControlJsonContext.Default.AppControlDiscoveryWarningArray) });
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(35));
        using var pipe = new NamedPipeClientStream(".", host.PipeName, PipeDirection.InOut, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
        try
        {
            await pipe.ConnectAsync(2000, deadline.Token).ConfigureAwait(false);
            if (!Windows.Win32.PInvoke.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var actual))
                throw new Win32Exception(Marshal.GetLastPInvokeError());
            if (actual != host.ProcessId)
                throw new LauncherOperationException("APP_CONTROL_IDENTITY_MISMATCH", "The pipe server is not the selected App process.");
            var requestId = Guid.NewGuid().ToString("N");
            var frame = new AppControlRequestFrame(1, host.Id, host.Nonce, requestId, request);
            await FramedJsonPipe.WriteAsync(pipe,
                JsonSerializer.SerializeToNode(frame, AppControlWireJsonContext.Default.AppControlRequestFrame)!.AsObject(),
                "APP_CONTROL", deadline.Token).ConfigureAwait(false);
            var response = await FramedJsonPipe.ReadAsync(pipe, "APP_CONTROL", deadline.Token).ConfigureAwait(false);
            var result = response.Deserialize(AppControlWireJsonContext.Default.AppControlResponseFrame)
                ?? throw new InvalidDataException("The App returned an empty response.");
            if (result.HostId != host.Id || result.RequestId != requestId)
                throw new LauncherOperationException("APP_CONTROL_IDENTITY_MISMATCH", "The App response identity does not match.");
            if (result.Status != "succeeded")
            {
                throw new LauncherOperationException(result.Error?.Code ?? "APP_CONTROL_FAILED",
                    result.Error?.Message ?? "The App operation failed.", result.Data?.DeepClone());
            }

            return result.Data as JsonObject ?? throw new InvalidDataException("The App returned a non-object payload.");
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            throw new LauncherOperationException("APP_CONTROL_TIMEOUT", "The App did not finish within its deadline. No operation was replayed.");
        }
        catch (TimeoutException)
        {
            throw new LauncherOperationException("APP_CONTROL_UNAVAILABLE", "The selected App is no longer accepting control requests.");
        }
        catch (IOException)
        {
            throw new LauncherOperationException("APP_CONTROL_DISCONNECTED", "The App disconnected. Inspect its current state before retrying; mutation outcome may be unknown.");
        }
    }

}