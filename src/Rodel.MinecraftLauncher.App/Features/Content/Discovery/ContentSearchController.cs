// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed record ContentSearchHit(
    string Id,
    string Name,
    string Description,
    string? IconUrl,
    ContentKind Kind);

internal sealed record ContentLocalMatch(ContentItem Item, ContentKind Kind);

internal enum ContentSearchNoticeKind
{
    Start,
    Empty,
    Failed
}

internal sealed record ContentSearchState
{
    internal string InstanceId { get; init; } = string.Empty;
    internal ContentKind? Kind { get; init; }
    internal string Query { get; init; } = string.Empty;
    internal IReadOnlyList<ContentSearchHit> Hits { get; init; } = [];
    internal IReadOnlyList<ContentLocalMatch> Local { get; init; } = [];
    internal IReadOnlySet<string> InstalledProjects { get; init; } = new HashSet<string>();
    internal bool Loading { get; init; }
    internal string Error { get; init; } = string.Empty;
    internal string LocalError { get; init; } = string.Empty;
    internal bool ContextUnknown { get; init; }
    internal string? MinecraftVersion { get; init; }
    internal string? Loader { get; init; }
    internal int Offset { get; init; }
    internal int Total { get; init; }
    internal int LastPageCount { get; init; }
    internal bool HasMore => LastPageCount > 0 && Offset < Total;
    internal bool CanLoadMore => HasMore && !Loading && (Error.Length == 0 || Error == LocalError);
    internal ContentSearchNoticeKind? Notice => string.IsNullOrWhiteSpace(Query)
        ? ContentSearchNoticeKind.Start
        : Loading
        ? null
        : Error.Length > 0 && Error != LocalError
        ? ContentSearchNoticeKind.Failed
        : Hits.Count == 0 ? ContentSearchNoticeKind.Empty : null;
}

internal sealed class ContentSearchController(LauncherClient client, ContentPlatformService? platform = null) : IDisposable
{
    private readonly ContentPlatformService? _platform = platform;
    private CancellationTokenSource? _request;
    private long _revision;
    private bool _disposed;
    internal ContentSearchState State { get; private set; } = new();
    internal event Action<ContentSearchState>? Changed;

    private void Publish(ContentSearchState state)
    {
        State = state;
        Changed?.Invoke(state);
    }

    internal async Task SearchAsync(
        string id,
        ContentKind? kind,
        string query,
        bool append = false)
    {
        if (_disposed)
            return;
        query = query.Trim();
        if (append && (!State.CanLoadMore || State.InstanceId != id || State.Kind != kind || State.Query != query))
            return;
        append &= State.InstanceId == id && State.Kind == kind && State.Query == query;
        var revision = ++_revision;
        _request?.Cancel();
        _request?.Dispose();
        _request = new();
        var ct = _request.Token;
        var previous = append ? State : new ContentSearchState();
        Publish(previous with
        {
            InstanceId = id,
            Kind = kind,
            Query = query,
            Loading = query.Length > 0,
            Error = previous.LocalError
        });
        if (!State.Loading)
            return;
        try
        {
            if (!append)
                await Task.Delay(350, ct);
            var context = id.Length > 0 ? await Task.Run(() => client.GetContentContextAsync(id, ct: ct), ct) : null;
            var unknown = id.Length == 0 || context?.Source == "launch-version-fallback";
            var minecraft = unknown ? null : context?.MinecraftVersion;
            var recordedLoader = context?.Loader;
            var loader = recordedLoader is "fabric" or "quilt" or "forge" or "neoforge" ? recordedLoader : null;
            var local = new List<ContentLocalMatch>();
            var installed = new HashSet<string>(StringComparer.Ordinal);
            var warnings = new List<string>();
            if (id.Length > 0 && !append)
            {
                foreach (var category in Enum.GetValues<ContentKind>())
                {
                    try
                    {
                        var entries = await client.ListContentAsync(id, new() { Kind = category.Directory() }, ct);
                        foreach (var entry in entries)
                        {
                            if (entry.ReadWarning is { } warning)
                                warnings.Add($"{entry.FileName}: {warning}");
                            if (!entry.IsContent)
                                continue;
                            var item = ContentItem.Read(entry);
                            if (item.ProjectId is { } project)
                                installed.Add(project);
                            if ((kind is null || category == kind) && (item.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || item.FileName.Contains(query, StringComparison.OrdinalIgnoreCase)))
                            {
                                local.Add(new(item, category));
                            }
                        }
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
                    {
                        ct.ThrowIfCancellationRequested();
                        warnings.Add($"{category.Directory()}: {error.Message}");
                    }
                }
            }

            if (ct.IsCancellationRequested || revision != _revision)
                return;
            if (!append)
            {
                Publish(State with
                {
                    Local = local,
                    InstalledProjects = installed,
                    ContextUnknown = unknown,
                    MinecraftVersion = minecraft,
                    Loader = loader,
                    LocalError = string.Join(Environment.NewLine, warnings),
                    Error = string.Join(Environment.NewLine, warnings)
                });
            }

            var platform = _platform ?? client.ContentPlatform;
            var result = await Task.Run(() => platform.SearchAsync(
                    platform: "modrinth",
                    query: query,
                    projectType: kind?.ProjectType() ?? "all",
                    ct: ct,
                    offset: previous.Offset,
                    limit: 20), ct);
            if (result["hits"] is not JsonArray hits)
                throw new InvalidDataException("Modrinth did not return a result list.");
            if (hits.OfType<JsonObject>().Any(hit => hit["project_type"]?.ToString() is not ("mod" or "resourcepack" or "shader") || (kind is { } filter && hit["project_type"]!.ToString() != filter.ProjectType())))
            {
                throw new InvalidDataException("Modrinth returned a project with a different content type than requested.");
            }

            var rows = hits.OfType<JsonObject>().Select(hit => new ContentSearchHit(
                    hit["project_id"]?.GetValue<string>() ?? throw new InvalidDataException("A content project ID is missing."),
                    hit["title"]?.ToString() ?? hit["project_id"]!.ToString(),
                    hit["description"]?.ToString() ?? string.Empty,
                    hit["icon_url"]?.ToString(),
                    hit["project_type"]!.ToString() switch
                    {
                        "resourcepack" => ContentKind.ResourcePacks,
                        "shader" => ContentKind.Shaders,
                        _ => ContentKind.Mods,
                    })).ToArray();
            if (ct.IsCancellationRequested || revision != _revision)
                return;
            Publish(State with
            {
                Hits = [.. previous.Hits.Concat(rows).DistinctBy(hit => hit.Id)],
                Loading = false,
                Offset = previous.Offset + hits.Count,
                Total = result["total_hits"]?.GetValue<int>() ?? (previous.Offset + hits.Count),
                LastPageCount = hits.Count,
                ContextUnknown = unknown,
                MinecraftVersion = minecraft,
                Loader = loader,
                Local = append ? previous.Local : local,
                InstalledProjects = append ? previous.InstalledProjects : installed,
            });
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            if (!ct.IsCancellationRequested && revision == _revision)
            {
                Publish(State with
                {
                    Loading = false,
                    Error = string.Join(Environment.NewLine, new[] { State.LocalError, exception.Message }.Where(message => message.Length > 0))
                });
            }
        }
    }

    internal void Suspend()
    {
        if (_disposed)
            return;
        ++_revision;
        _request?.Cancel();
        if (State.Loading)
            Publish(State with { Loading = false });
    }

    internal async Task RefreshInstalledAsync(string id)
    {
        var snapshot = State;
        if (_disposed || id.Length == 0 || snapshot.InstanceId != id)
            return;
        try
        {
            var installed = await Task.Run(async () =>
                {
                    var projects = new HashSet<string>(StringComparer.Ordinal);
                    foreach (var kind in Enum.GetValues<ContentKind>())
                    {
                        foreach (var entry in await client.ListContentAsync(id, new() { Kind = kind.Directory() }).ConfigureAwait(false))
                        {
                            if (entry.IsContent && entry.Tracking?["projectId"]?.ToString() is { } project)
                                projects.Add(project);
                        }
                    }

                    return projects;
                });
            if (!_disposed && State.InstanceId == id && State.Query == snapshot.Query)
                Publish(State with { InstalledProjects = installed });
        }
        catch (Exception error)
        {
            if (!_disposed && State.InstanceId == id)
                Publish(State with { Error = error.Message });
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _request?.Cancel();
        _request?.Dispose();
    }
}