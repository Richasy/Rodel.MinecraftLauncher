// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Tasks;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.Core.Application;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

/// <summary>Owns online installations for the content page independently of any open dialog.</summary>
internal sealed class ContentDownloads(LauncherClient client, HomeController home) : IDisposable
{
    private readonly List<ContentDownload> _entries = [];
    private bool _disposed;
    internal event Action? Changed;
    internal event Action<ContentDownload>? Completed;
    internal IReadOnlyList<ContentDownload> Items { get; private set; } = [];
    internal bool Active => _entries.Any(entry => entry.Task.Active);
    internal int ActiveCount { get; private set; }
    internal bool HasActive(string instanceId) => _entries.Any(entry => entry.Task.Active && entry.Install.Target.Id == instanceId);

    internal ContentDownload Create(ContentSearchHit project, ContentSearchState context, ContentInstallTarget target)
    {
        var existing = _entries.FirstOrDefault(entry => entry.Task.Active && entry.Project.Id == project.Id && entry.Install.Target.Id == target.Id);
        if (existing is not null) return existing;
        var reader = new ContentInstallReader(client, project.Id, context.MinecraftVersion, context.Loader);
        var entry = new ContentDownload(project, new(project.Id, project.Kind, target, reader.LoadAsync, reader.ReviewAsync), new(client, home));
        _entries.Add(entry);
        entry.Task.Changed += () => OnChanged(entry);
        return entry;
    }

    internal async Task StartAsync(ContentDownload entry)
    {
        if (entry.Task.Active || entry.Install.TakeReview() is not { } binding) return;
        entry.Started = true;
        entry.Dismissed = false;
        await entry.Task.RunAsync(new ApplyContentReviewTask(binding.InstanceId, binding.ReviewId), refreshHome: false);
        if (_disposed) return;
        Completed?.Invoke(entry);
        if (!entry.HasDialog) Release(entry);
        await home.RefreshAsync();
    }

    internal void Dismiss(ContentDownload entry)
    {
        if (entry.Task.Active) return;
        entry.Dismissed = true;
        Release(entry);
        NotifyListChanged();
    }

    internal void Release(ContentDownload entry)
    {
        if (entry.Task.Active || entry.HasDialog || (entry.Started && !entry.Dismissed && entry.Task.Outcome is not ("succeeded" or "canceled"))) return;
        if (_entries.Remove(entry)) entry.Dispose();
    }

    private void OnChanged(ContentDownload entry)
    {
        entry.ObserveProgress();
        NotifyListChanged();
    }

    private void NotifyListChanged()
    {
        var visible = _entries.Where(entry => entry.Started && !entry.Dismissed && entry.Task.Outcome is not ("succeeded" or "canceled")).ToArray();
        var active = _entries.Count(entry => entry.Task.Active);
        if (active == ActiveCount && Items.SequenceEqual(visible)) return;
        Items = visible;
        ActiveCount = active;
        Changed?.Invoke();
    }

    /// <summary>Stops observing owned tasks when the host exits; task execution remains owned by Core.</summary>
    public void Dispose()
    {
        _disposed = true;
        foreach (var entry in _entries) entry.Dispose();
        _entries.Clear();
    }
}

/// <summary>Keeps the frozen selection, task receipt and transfer sample when its dialog is minimized.</summary>
internal sealed class ContentDownload(ContentSearchHit project, ContentInstallStore install, FeatureTaskController task) : IDisposable
{
    private string? _path;
    private long _bytes;
    private long _sample = Stopwatch.GetTimestamp();
    private double _speed;
    internal string Id { get; } = Guid.NewGuid().ToString("N");
    internal ContentSearchHit Project { get; } = project;
    internal ContentInstallStore Install { get; } = install;
    internal FeatureTaskController Task { get; } = task;
    internal bool Started { get; set; }
    internal bool Dismissed { get; set; }
    internal bool Loaded { get; set; }
    internal bool HasDialog { get; set; }
    internal string Speed => string.Format(Resources.ContentDownloadSpeed, AppImageCache.FormatSize((long)_speed));
    internal ContentState State => new()
    {
        Active = Task.Active,
        CancelRequested = Task.CancelRequested,
        Outcome = Task.Outcome,
        Job = Task.Job,
        OperationInstance = Install.Target.Name,
        OperationKind = Project.Kind,
        Failure = Task.Error.Length == 0 ? null : new JsonObject { ["error"] = new JsonObject { ["message"] = Task.Error } },
    };

    internal void ObserveProgress()
    {
        var now = Stopwatch.GetTimestamp();
        var progress = Task.Job?.Progress;
        var bytes = progress?.Bytes ?? 0;
        var elapsed = Stopwatch.GetElapsedTime(_sample, now).TotalSeconds;
        if (Task.Active && progress?.Path == _path && elapsed < 1) return;
        _speed = Task.Active && progress?.Stage == "download" && progress.Reused != true && progress.Path == _path && bytes >= _bytes && elapsed > 0
            ? (bytes - _bytes) / elapsed : 0;
        _path = progress?.Path;
        _bytes = bytes;
        _sample = now;
    }

    /// <summary>Releases selection requests and task observation when the owning content page is disposed.</summary>
    public void Dispose()
    {
        Install.Dispose();
        Task.Dispose();
    }
}