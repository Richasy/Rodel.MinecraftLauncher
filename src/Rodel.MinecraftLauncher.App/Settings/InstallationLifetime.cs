// Copyright (c) Richasy. All rights reserved.

using Microsoft.Win32;
using Sprout;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.App.Settings;

/// <summary>Holds the installed process's uninstall guard and forwards cooperative exit requests to its UI.</summary>
internal sealed class InstallationLifetime : IDisposable
{
    private static readonly System.Buffers.SearchValues<char> s_myChars = System.Buffers.SearchValues.Create("0123456789abcdefABCDEF");
    private readonly Lock _gate = new();
    private readonly Mutex _running;
    private readonly EventWaitHandle _close;
    private readonly CancellationTokenSource _lifetime = new();
    private readonly CancellationToken _token;
    private RegisteredWaitHandle? _wait;
    private Action<Action>? _post;
    private Action? _exit;
    private bool _pending;
    private bool _disposed;

    internal InstallationLifetime(string name)
    {
        _token = _lifetime.Token;
        _running = new(false, name);
        _close = new(false, EventResetMode.ManualReset, name + ".Close");
        Arm();
    }

    internal static InstallationLifetime? Open()
    {
        if (PackageIdentity.IsPackaged)
            return null;
        using var registry = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64);
        using var installation = registry.OpenSubKey(@"Software\Richasy\RMCL\Installations\" + RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant());
        if (installation?.GetValue("Path") is not string path
            || !string.Equals(Path.TrimEndingDirectorySeparator(path), Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), StringComparison.OrdinalIgnoreCase)
            || installation.GetValue("Mutex") is not string name
            || !name.StartsWith(@"Local\Richasy.RMCL.", StringComparison.Ordinal)
            || name.Length != @"Local\Richasy.RMCL.".Length + 64
            || name.AsSpan(@"Local\Richasy.RMCL.".Length).ContainsAnyExcept(s_myChars))
        {
            return null;
        }

        return new(name);
    }

    internal void Attach(Action<Action> post, Action exit)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _post = post;
            _exit = exit;
            if (_pending)
                PostExit();
        }
    }

    private void Arm()
    {
        lock (_gate)
        {
            if (!_disposed)
                _wait = ThreadPool.RegisterWaitForSingleObject(_close, OnRequested, null, Timeout.Infinite, executeOnlyOnce: true);
        }
    }

    private void OnRequested(object? state, bool timedOut)
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _pending = true;
            PostExit();
        }

        _ = RearmAsync();
    }

    private void PostExit()
    {
        if (_post is null)
            return;
        _pending = false;
        _post(() =>
        {
            lock (_gate)
            {
                if (!_disposed)
                    _exit?.Invoke();
            }
        });
    }

    private async Task RearmAsync()
    {
        try
        {
            // A manual-reset pulse reaches every process of this installation, including tray instances.
            while (true)
            {
                await Task.Delay(100, _token).ConfigureAwait(false);
                lock (_gate)
                {
                    if (_disposed)
                        return;
                    if (!_close.WaitOne(0))
                        break;
                }
            }

            Arm();
        }
        catch (OperationCanceledException) when (_token.IsCancellationRequested)
        {
        }
    }

    /// <summary>Stops receiving requests and releases the running marker only after the application's run loop ends.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _wait?.Unregister(null);
            _lifetime.Cancel();
            _close.Dispose();
            _running.Dispose();
            _lifetime.Dispose();
            _exit = null;
            _post = null;
        }
    }
}