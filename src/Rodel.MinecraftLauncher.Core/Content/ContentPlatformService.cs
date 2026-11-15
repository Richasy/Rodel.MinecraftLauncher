// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentPlatformService
{
    private readonly LauncherPaths _paths;
    private readonly HttpClient _http;
    private readonly Uri _modrinth;
    private readonly DownloadService _downloads;
    private readonly ContentService _content;
    private readonly TimeSpan _metadataTimeout;
    public ContentPlatformService(LauncherPaths paths, HttpClient? httpClient = null, Uri? modrinthBaseUri = null, ICurseForgeProvider? curseForgeProvider = null, TimeSpan? metadataTimeout = null)
    {
        _paths = paths;
        _metadataTimeout = metadataTimeout ?? NetworkSettings.MetadataTimeout(paths);
        _http = httpClient ?? NetworkSettings.Client(paths, TimeSpan.FromMinutes(5));
        _modrinth = modrinthBaseUri ?? new Uri("https://api.modrinth.com/v2/");
        CurseForge = curseForgeProvider;
        _downloads = new(paths, _http);
        _content = new(paths);
    }

    /// <summary>Searches the selected content provider with explicit compatibility filters, ordering, and upstream pagination.</summary>
    public async Task<JsonObject> SearchAsync(string platform, string query, string projectType = "mod", int offset = 0, int limit = 20, string? minecraftVersion = null, string? loader = null, string index = "relevance", CancellationToken ct = default)
    {
        if (offset < 0 || limit is < 1 or > 100)
            throw new ArgumentOutOfRangeException(nameof(limit), "Offset must be nonnegative and limit 1..100.");
        if (index is not ("relevance" or "downloads" or "follows" or "newest" or "updated"))
            throw new ArgumentException("Unknown search ordering.");
        platform = Normalize(platform);
        if (platform == "curseforge")
            return await CurseForge.SearchAsync(query, projectType, ct).ConfigureAwait(false);
        var types = projectType == "all" ? new[] { "mod", "resourcepack", "shader" } : [projectType];
        var facets = new JsonArray((JsonNode)new JsonArray([.. types.Select(type => (JsonNode?)JsonValue.Create("project_type:" + type))]));
        if (minecraftVersion is not null)
            facets.Add((JsonNode)new JsonArray(JsonValue.Create("versions:" + minecraftVersion)));
        if (loader is not null)
            facets.Add((JsonNode)new JsonArray(JsonValue.Create("categories:" + loader)));
        var route = "search?query=" + Encode(query) + "&offset=" + offset + "&limit=" + limit + "&index=" + Encode(index) + "&facets=" + Encode(facets.ToJsonString());
        var result = await ApiAsync(route, ct).ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException("Platform search did not return an object.");
        result["requestedOffset"] = offset;
        result["requestedLimit"] = limit;
        return result;
    }

    public async Task<JsonObject> GetProjectAsync(string platform, string id, CancellationToken ct = default)
    {
        platform = Normalize(platform);
        if (platform == "curseforge")
            return await CurseForge.GetProjectAsync(id, ct).ConfigureAwait(false);
        var response = await ApiAsync("project/" + Encode(id), ct).ConfigureAwait(false);
        return response.DeepClone() as JsonObject ?? throw new InvalidDataException($"Platform project '{id}' is missing.");
    }

    public async Task<JsonArray> GetVersionsAsync(string platform, string id, string? minecraftVersion = null, string? loader = null, CancellationToken ct = default)
    {
        platform = Normalize(platform);
        if (platform == "curseforge")
            return await CurseForge.GetVersionsAsync(id, minecraftVersion, loader, ct).ConfigureAwait(false);
        if (platform == "modrinth")
        {
            var query = new List<string>();
            if (minecraftVersion is not null)
                query.Add("game_versions=" + Encode(new JsonArray(JsonValue.Create(minecraftVersion)).ToJsonString()));
            if (loader is not null)
                query.Add("loaders=" + Encode(new JsonArray(JsonValue.Create(loader)).ToJsonString()));
            return await ApiAsync("project/" + Encode(id) + "/version" + (query.Count > 0 ? "?" + string.Join('&', query) : string.Empty), ct).ConfigureAwait(false) as JsonArray ?? throw new InvalidDataException("Platform versions did not return an array.");
        }

        throw new ArgumentException($"Unknown content platform: {platform}");
    }

    [AllowNull]
    private ICurseForgeProvider CurseForge => field ?? throw new ContentPlatformUnavailableException();

    private async Task<JsonNode> ApiAsync(string route, CancellationToken ct)
    {
        var uri = new Uri(_modrinth, route);
        var originalOnly = false;
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(_metadataTimeout);
                using var response = await DownloadRequests.SendAsync(client: _http, uri: uri, headersForUri: null, ct: deadline.Token, configure: request => request.Options.Set(DownloadRequests.OriginalSourceOnly, originalOnly)).ConfigureAwait(false);
                if ((response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500) && attempt < 3)
                {
                    var delay = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(delay.TotalSeconds, 0.1, 30)), ct).ConfigureAwait(false);
                    continue;
                }

                response.EnsureSuccessStatusCode();
                var stream = (await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false));
                await using var streamDisposal = stream.ConfigureAwait(false);
                return await JsonNode.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false) ?? throw new InvalidDataException("Empty content platform response.");
            }
            catch (System.Text.Json.JsonException) when (!originalOnly && attempt < 3)
            {
                originalOnly = true;
                await Task.Delay(TimeSpan.FromMilliseconds(100), ct).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not HttpRequestException { StatusCode: not null } && (ex is HttpRequestException or IOException || (ex is OperationCanceledException && !ct.IsCancellationRequested)) && attempt < 3)
            {
                originalOnly = true;
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new HttpRequestException($"Content metadata request/body exceeded {_metadataTimeout} after bounded retries: {uri}", ex);
            }
        }
    }

    private static string SafeCachePath(string root, string filename)
    {
        var full = Path.GetFullPath(Path.Combine(root, filename));
        if (!full.StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(root)) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Platform cache path escapes its directory.");
        return full;
    }

    private static string Normalize(string platform) => platform.ToLowerInvariant() switch
    {
        "modrinth" => "modrinth",
        "curseforge" => "curseforge",
        _ => throw new ArgumentException($"Unknown content platform: {platform}")
    };
    private static string Encode(string value) => Uri.EscapeDataString(value);
}