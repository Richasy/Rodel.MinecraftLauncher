// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Settings;
using Sprout.Reactive;
using System.Net.Http;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

/// <summary>Owns one window's optional update discovery, cancellable download and explicit version dismissal.</summary>
[Store]
internal sealed partial class UpdateState
{
    private readonly AppSettings _settings;
    private readonly GitHubUpdateClient _client;
    private readonly HttpClient _http;
    private readonly CancellationToken _lifetime;
    private readonly string _cacheDirectory;
    private readonly Version _currentVersion;
    private Action? _cancelDownload;
    private bool _downloadAuthorized;

    internal UpdateState(AppSettings settings, HttpClient? http = null, string? cacheDirectory = null, Version? currentVersion = null)
    {
        _settings = settings;
        _lifetime = Lifetime;
        _http = http ?? new() { Timeout = Timeout.InfiniteTimeSpan };
        _client = new(_http);
        _cacheDirectory = cacheDirectory ?? Path.Combine(AppStorage.CachePath, "Updates");
        _currentVersion = currentVersion ?? typeof(UpdateState).Assembly.GetName().Version!;
    }

    internal partial AvailableUpdate? Available { get; private set; }
    internal partial long Received { get; private set; }
    internal partial bool Downloading { get; private set; }
    internal partial bool Failed { get; private set; }
    internal partial bool Blocked { get; private set; }
    internal partial bool Installing { get; private set; }
    internal string? DownloadedPath { get; private set; }

    internal async Task CheckAsync()
    {
        var architecture = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => string.Empty,
        };
        try
        {
            UpdateInstaller.CleanupInstalled(_cacheDirectory, _currentVersion);
            var update = await _client.CheckAsync(_currentVersion, _settings.SkippedUpdateVersion, architecture, _lifetime);
            if (!_lifetime.IsCancellationRequested)
                Available = update;
        }
        catch (Exception)
        {
            // Optional update state, including unreadable preferences, cannot fail application startup.
        }
    }

    internal async Task<string?> DownloadAsync()
    {
        if (_lifetime.IsCancellationRequested || Available is not { } update || Downloading || Installing)
            return null;
        Failed = false;
        Blocked = false;
        Downloading = true;
        _downloadAuthorized = false;
        DownloadedPath = null;
        Received = 0;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(_lifetime);
        Action cancel = cancellation.Cancel;
        _cancelDownload = cancel;
        try
        {
            DownloadedPath = await _client.DownloadAsync(update, Path.Combine(_cacheDirectory, Guid.NewGuid().ToString("N")),
                new Progress<long>(value =>
                {
                    if (!_lifetime.IsCancellationRequested && ReferenceEquals(_cancelDownload, cancel))
                        Received = value;
                }), cancellation.Token);
            if (cancellation.IsCancellationRequested)
            {
                DiscardDownload();
                return null;
            }
            _downloadAuthorized = true;
            return DownloadedPath;
        }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
        {
            return null;
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested)
                Failed = true;
            return null;
        }
        finally
        {
            _cancelDownload = null;
            if (!_lifetime.IsCancellationRequested)
                Downloading = false;
        }
    }

    internal bool Skip()
    {
        if (Available is null || Downloading || Installing)
            return false;
        try
        {
            _settings.SkippedUpdateVersion = Available.Manifest.Version;
            Available = null;
            return true;
        }
        catch (Exception)
        {
            Failed = true;
            return false;
        }
    }

    internal void Cancel()
    {
        _downloadAuthorized = false;
        _cancelDownload?.Invoke();
        if (!Downloading && !Installing)
            DiscardDownload();
    }

    internal async Task InstallAsync(string path, Func<string, string, Task<bool>> install)
    {
        if (_lifetime.IsCancellationRequested || !_downloadAuthorized || path != DownloadedPath || Available is not { } update || Installing)
            return;
        _downloadAuthorized = false;
        Installing = true;
        try
        {
            var closed = await install(path, update.Installer.Sha256);
            if (!_lifetime.IsCancellationRequested)
            {
                Blocked = !closed;
                if (!closed)
                    DiscardDownload();
            }
        }
        catch (Exception)
        {
            if (!_lifetime.IsCancellationRequested)
            {
                Failed = true;
                DiscardDownload();
            }
        }
        finally
        {
            if (!_lifetime.IsCancellationRequested)
                Installing = false;
        }
    }

    partial void OnDispose()
    {
        _cancelDownload?.Invoke();
        _http.Dispose();
    }

    private void DiscardDownload()
    {
        if (DownloadedPath is not { } path)
            return;
        try { File.Delete(path); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        DownloadedPath = null;
    }
}
