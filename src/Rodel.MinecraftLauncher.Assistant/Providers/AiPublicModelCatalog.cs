// Copyright (c) Richasy. All rights reserved.

using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Anonymous public metadata transport and last-good local snapshots; Core chooses the storage root.</summary>
internal sealed class AiPublicModelCatalog
{
    private static readonly ConcurrentDictionary<string, AiPublicModelCatalog> Instances = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HttpClient Http = new(new HttpClientHandler { AllowAutoRedirect = false }) { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly Lazy<JsonObject> Bundled = new(() =>
    {
        using var stream = typeof(AiPublicModelCatalog).Assembly.GetManifestResourceStream("Rodel.ModelsDev.json")!;
        return JsonNode.Parse(stream)!.AsObject();
    });
    private readonly string _directory;
    private readonly HttpClient _http;
    private readonly ConcurrentDictionary<string, DateTimeOffset> _logoRetry = new();
    private readonly Lock _gate = new();
    private JsonObject _snapshot;
    private DateTimeOffset _nextCheck;
    private Task? _refresh;

    internal AiPublicModelCatalog(string directory, HttpClient? http = null)
    {
        _directory = directory;
        _http = http ?? Http;
        _snapshot = Bundled.Value;
        try
        {
            var file = Path.Combine(directory, "snapshot.json");
            if (File.Exists(file) && new FileInfo(file).Length <= 16 * 1024 * 1024)
            {
                if (JsonNode.Parse(File.ReadAllText(file)) is JsonObject saved && Valid(saved)) _snapshot = saved;
            }

            if (DateTimeOffset.TryParse(_snapshot["fetchedUtc"]?.ToString(), out var fetched)) _nextCheck = fetched.AddDays(1);
        }
        catch (Exception error) when (OptionalFailure(error)) { }
    }

    /// <summary>Gets the shared metadata cache for an exact Core-selected directory.</summary>
    public static AiPublicModelCatalog For(string directory) => Instances.GetOrAdd(directory, static path => new(path));

    /// <summary>Returns an immutable snapshot immediately; a due anonymous refresh runs independently of generation.</summary>
    public JsonObject Read(bool refresh = false)
    {
        lock (_gate)
        {
            if (refresh && DateTimeOffset.UtcNow >= _nextCheck && (_refresh is null || _refresh.IsCompleted))
            {
                _nextCheck = DateTimeOffset.UtcNow.AddHours(1);
                _refresh = Task.Run(RefreshAsync);
            }

            return _snapshot;
        }
    }

    internal async Task RefreshAsync()
    {
        try
        {
            var bytes = await DownloadAsync("api.json", 16 * 1024 * 1024, CancellationToken.None).ConfigureAwait(false);
            if (JsonNode.Parse(bytes) is not JsonObject root) throw new JsonException("The public catalog must be an object.");
            var providers = new JsonArray();
            foreach (var pair in root)
            {
                if (pair.Value is not JsonObject provider || provider["models"] is not JsonObject models || !SafeId(pair.Key)) continue;
                var items = new JsonArray();
                foreach (var model in models)
                {
                    if (model.Value is not JsonObject value || value["limit"] is not JsonObject limit) continue;
                    var context = Positive(limit["context"]);
                    if (context is null) continue;
                    items.Add((JsonNode)new JsonObject
                    {
                        ["id"] = model.Key,
                        ["context"] = context,
                        ["input"] = Positive(limit["input"]),
                        ["output"] = Positive(limit["output"]),
                        ["canonical"] = value["canonical_model_id"]?.ToString()
                    });
                }

                providers.Add((JsonNode)new JsonObject { ["id"] = pair.Key, ["api"] = provider["api"]?.ToString(), ["models"] = items });
            }

            if (providers.Count == 0 || !providers.Any(provider => provider!["models"]!.AsArray().Count > 0)) throw new JsonException("The public model catalog is empty.");
            var next = new JsonObject
            {
                ["schemaVersion"] = 1,
                ["source"] = "https://models.dev/api.json",
                ["fetchedUtc"] = DateTimeOffset.UtcNow.ToString("O"),
                ["providers"] = providers
            };
            await SaveAsync(Path.Combine(_directory, "snapshot.json"), System.Text.Encoding.UTF8.GetBytes(next.ToJsonString()), CancellationToken.None).ConfigureAwait(false);
            lock (_gate) { _snapshot = next; _nextCheck = DateTimeOffset.UtcNow.AddDays(1); }
        }
        catch (Exception error) when (OptionalFailure(error)) { }
    }

    /// <summary>Finds exact IDs; cross-host public information stays advisory and never supplies a deployment limit.</summary>
    public static (long? Context, long? Input, string? Source, bool MatchedProvider) Find(string model, Uri? endpoint, JsonObject? snapshot = null)
    {
        var matches = Matches(model, endpoint, snapshot ?? Bundled.Value).ToArray();
        var hosted = matches.Where(item => item.Host).ToArray();
        if (hosted.Length > 0) matches = hosted;
        if (matches.Length == 0) return (null, null, null, false);
        // The conservative public reference may describe another deployment; consumers must retain MatchedProvider.
        return (matches.Min(item => Positive(item.Model["context"])), matches.Min(item => Positive(item.Model["input"])),
            "models.dev_snapshot:" + matches[0].Provider["id"], hosted.Length > 0);
    }

    /// <summary>Returns an unambiguous output capacity only for the exact model and provider host; unknown deployments keep their own defaults.</summary>
    public static int? OutputLimit(string model, Uri? endpoint, JsonObject? snapshot = null)
    {
        var limits = Matches(model, endpoint, snapshot ?? Bundled.Value).Where(item => item.Host)
            .Select(item => Positive(item.Model["output"])).Where(value => value is > 0 and <= int.MaxValue).Distinct().ToArray();
        return limits.Length == 1 ? (int)limits[0]!.Value : null;
    }

    /// <summary>Resolves a genuine provider logo by host, otherwise a model author's canonical lab identity.</summary>
    public async Task<(string? Provider, string? Lab, string? Svg)> IdentityAsync(string model, Uri? endpoint, CancellationToken ct)
    {
        var snapshot = Read(refresh: true);
        var provider = snapshot["providers"]!.AsArray().FirstOrDefault(item => Host(item, endpoint))?["id"]?.ToString();
        var labs = Matches(model, endpoint, snapshot).Select(item => item.Model["canonical"]?.ToString()?.Split('/')[0])
            .Where(item => !string.IsNullOrEmpty(item)).Distinct(StringComparer.Ordinal).ToArray();
        var lab = labs.Length == 1 ? labs[0] : null;
        var id = provider ?? lab;
        if (id is null || !SafeId(id)) return (provider, lab, null);
        var file = Path.Combine(_directory, "logos", (provider is null ? "lab-" : "provider-") + id + ".svg");
        try
        {
            if ((!File.Exists(file) || File.GetLastWriteTimeUtc(file) < DateTime.UtcNow.AddDays(-7))
                && (!_logoRetry.TryGetValue(file, out var retry) || retry <= DateTimeOffset.UtcNow))
            {
                _logoRetry[file] = DateTimeOffset.UtcNow.AddHours(1);
                var data = await DownloadAsync("logos/" + (provider is null ? "labs/" : string.Empty) + id + ".svg", 256 * 1024, ct).ConfigureAwait(false);
                var text = System.Text.Encoding.UTF8.GetString(data);
                var xml = System.Xml.Linq.XDocument.Parse(text);
                if (xml.Root?.Name.LocalName != "svg" || xml.Descendants().Any(element => element.Name.LocalName is "script" or "foreignObject" or "image"
                    || element.Attributes().Any(attribute => attribute.Name.LocalName.StartsWith("on", StringComparison.OrdinalIgnoreCase)
                        || (attribute.Name.LocalName == "href" && !attribute.Value.StartsWith('#')))))
                {
                    throw new JsonException("Unsupported logo content.");
                }

                await SaveAsync(file, data, ct).ConfigureAwait(false);
            }

            return (provider, lab, File.Exists(file) ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false) : null);
        }
        catch (Exception error) when (OptionalFailure(error))
        {
            try { return (provider, lab, File.Exists(file) ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false) : null); }
            catch (Exception readError) when (OptionalFailure(readError)) { return (provider, lab, null); }
        }
    }

    private static IEnumerable<(JsonNode Provider, JsonNode Model, bool Host)> Matches(string model, Uri? endpoint, JsonObject snapshot)
    {
        foreach (var provider in snapshot["providers"]!.AsArray())
        {
            foreach (var item in provider!["models"]!.AsArray())
                if (item!["id"]?.ToString() == model) yield return (provider, item, Host(provider, endpoint));
        }
    }

    private static bool Valid(JsonObject snapshot) => snapshot["providers"] is JsonArray { Count: > 0 } providers
        && providers.Any(provider => provider?["models"] is JsonArray { Count: > 0 })
        && providers.All(provider => provider is JsonObject && SafeId(provider["id"]?.ToString() ?? string.Empty)
            && provider["models"] is JsonArray models && models.All(model => model is JsonObject
                && model["id"] is JsonValue && Positive(model["context"]) is not null));

    private static bool Host(JsonNode? provider, Uri? endpoint) => endpoint is not null
        && Uri.TryCreate(provider?["api"]?.ToString(), UriKind.Absolute, out var api) && api.Host.Equals(endpoint.Host, StringComparison.OrdinalIgnoreCase);
    private static bool SafeId(string value) => value.Length is > 0 and < 100 && value.All(character => char.IsAsciiLetterOrDigit(character) || character is '-' or '_');
    private static long? Positive(JsonNode? node) => node is JsonValue value && value.TryGetValue<long>(out var number) && number > 0 ? number : null;
    private static bool OptionalFailure(Exception error) => error is IOException or UnauthorizedAccessException or HttpRequestException
        or OperationCanceledException or JsonException or InvalidOperationException or System.Xml.XmlException;
    private async Task<byte[]> DownloadAsync(string relative, int maximum, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        ct = deadline.Token;
        using var response = await _http.GetAsync(new Uri("https://models.dev/" + relative), HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength > maximum) throw new IOException("Public metadata exceeds its download limit.");
        await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var output = new MemoryStream();
        var buffer = new byte[16384];
        int read;
        while ((read = await stream.ReadAsync(buffer, ct).ConfigureAwait(false)) != 0)
        {
            if (output.Length + read > maximum) throw new IOException("Public metadata exceeds its download limit.");
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }
    private static async Task SaveAsync(string path, byte[] content, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { await File.WriteAllBytesAsync(temporary, content, ct).ConfigureAwait(false); File.Move(temporary, path, overwrite: true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }
}