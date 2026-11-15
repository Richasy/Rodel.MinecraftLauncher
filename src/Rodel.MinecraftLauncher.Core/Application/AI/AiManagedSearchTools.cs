// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiManagedSearchTools(LauncherPaths paths, JsonObject turn, AiManagedComponents components,
    Func<string, string?, JsonObject, Task> emit) : IAsyncDisposable
{
    private readonly AiToolPolicy _policy = new(paths, turn);
    private AiManagedMcpSession? _session;
    private AiWebEgressProxy? _proxy;
    private string? _toolName;

    public async Task<IReadOnlyList<AIFunctionDeclaration>> ConnectAsync(CancellationToken ct)
    {
        if (turn["networkAccessEnabled"]?.GetValue<bool>() != true) return [];
        try
        {
            _policy.RequireNetwork();
            var (component, executable) = await components.OpenAsync("rodel_search", ct).ConfigureAwait(false);
            _proxy = new AiWebEgressProxy();
            var proxy = "http://" + _proxy.Username + ":" + _proxy.Password + "@127.0.0.1:"
                + _proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
            _session = await AiManagedMcpSession.ConnectAsync(new AiMcpRegistration
            {
                Id = component.Id,
                DisplayName = "Rodel managed search API reader",
                ExecutablePath = executable,
                WorkingDirectory = Path.GetDirectoryName(executable),
                Arguments = ["-resolver", "cloudflare"]
            }, emit, privateEnvironment: new Dictionary<string, string>
            {
                ["HTTP_PROXY"] = proxy,
                ["HTTPS_PROXY"] = proxy,
                ["NO_PROXY"] = string.Empty
            }, privateSecrets: [_proxy.Password], ct: ct).ConfigureAwait(false);
            var tool = _session.Tools.SingleOrDefault(item => _session.ActualName(item.Name) == "doh_fetch")
                ?? throw new LauncherOperationException("AI_MANAGED_SEARCH_CATALOG_CHANGED", "The pinned API reader did not expose its expected actual fetch tool.");
            _toolName = tool.Name;
            await emit("managed_mcp_ready", "managed:search", new JsonObject
            {
                ["id"] = component.Id,
                ["version"] = component.Version,
                ["revision"] = component.Revision,
                ["actualTool"] = "doh_fetch",
                ["searchProvider"] = "Mwmbl public search API v2",
                ["api"] = "https://api.mwmbl.org/api/v2/search/",
                ["publicDnsProvider"] = "Cloudflare DNS",
                ["userSearchKeyRequired"] = false,
                ["processId"] = _session.ProcessId,
                ["toolCount"] = 1
            }).ConfigureAwait(false);
            return [AIFunctionFactory.CreateDeclaration(tool.Name,
                tool.Description + " Product-managed KEYLESS WEB SEARCH, through Mwmbl's intentionally public anonymous API, not scraped search HTML. Set url to https://api.mwmbl.org/api/v2/search/?q=YOUR_PERCENT_ENCODED_QUERY. Only this API host/path and one nonempty q are accepted. max_chars is a native UTF-8 byte limit bounded to 500..50000; resolver is fixed to Cloudflare. Results are actual JSON with title/url/content; the wrapper explicitly marks truncation. This is a byte-limited excerpt, NOT a server-side page: do not invent page boundaries or assume number_of_results equals the supplied count. Cite Mwmbl and original URLs. Its independent index is not exhaustive. Date/category/language filters are not implemented; Chinese query text is allowed. Retrieval time is not publication time. Use web_scrape to read pages. No player search key/configuration is needed.",
                tool.JsonSchema)];
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await emit("managed_mcp_unavailable", "managed:search", new JsonObject
            {
                ["id"] = "rodel_search",
                ["ordinaryConversationAvailable"] = true,
                ["recovery"] = "Retry or repair the product-managed search component. No player search key or endpoint is required.",
                ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
            }).ConfigureAwait(false);
            return [];
        }
    }

    public bool Contains(string name) => name == _toolName;

    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, string invocation, CancellationToken ct)
    {
        if (_session is null || _proxy is null || name != _toolName)
            throw new LauncherOperationException("AI_MANAGED_SEARCH_UNAVAILABLE", "The product-managed search component is not connected.");
        _policy.RequireNetwork();
        var raw = arguments["url"]?.GetValue<string>() ?? string.Empty;
        if (raw.Length > 2048 || !Uri.TryCreate(raw, UriKind.Absolute, out var url) || url.Scheme != "https"
            || url.DnsSafeHost != "api.mwmbl.org" || url.Port != 443 || url.AbsolutePath != "/api/v2/search/"
            || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
        {
            throw new LauncherOperationException("AI_SEARCH_API_SCOPE", "This search reader accepts only the product's public Mwmbl search API. Use the separate page reader for other URLs.");
        }

        var query = url.Query.TrimStart('?').Split('&');
        if (query.Length != 1 || !query[0].StartsWith("q=", StringComparison.Ordinal))
            throw new ArgumentException("Provide one q query parameter, without unsupported search filters.");
        var decoded = Uri.UnescapeDataString(query[0][2..].Replace('+', ' '));
        if (string.IsNullOrWhiteSpace(decoded) || decoded.Length > 512 || decoded.Any(char.IsControl))
            throw new ArgumentException("A search query must contain 1 through 512 non-control characters.");
        var maximum = arguments["max_chars"]?.GetValue<int>() ?? 30000;
        if (maximum is < 500 or > 50000) throw new ArgumentException("Request between 500 and 50000 native search-result bytes.");
        var supplied = arguments.DeepClone().AsObject();
        supplied["resolver"] = "cloudflare";
        supplied["max_chars"] = maximum;
        var rateFile = Path.Combine(paths.Root, "ai", "search-rate.json");
        using (await ResourceLease.AcquireAsync(rateFile, ct).ConfigureAwait(false))
        {
            if (File.Exists(rateFile))
            {
                var previous = JsonFiles.ReadObject(rateFile);
                var next = DateTimeOffset.Parse(previous["lastUtc"]!.ToString(), System.Globalization.CultureInfo.InvariantCulture).AddSeconds(3);
                var delay = next - DateTimeOffset.UtcNow;
                if (delay >= TimeSpan.FromMinutes(1))
                    throw new LauncherOperationException("AI_SEARCH_CLOCK_CHANGED", "The saved search time is ahead of the system clock. Correct the clock or wait; no upstream request was sent.");
                if (delay > TimeSpan.Zero) await Task.Delay(delay, ct).ConfigureAwait(false);
            }

            _policy.RequireNetwork();
            await JsonFiles.WriteAsync(rateFile, new JsonObject { ["lastUtc"] = DateTimeOffset.UtcNow.ToString("O") }, ct).ConfigureAwait(false);
        }

        var grant = new AiCallGrant(token =>
        {
            token.ThrowIfCancellationRequested();
            _policy.RequireNetwork();
            return Task.CompletedTask;
        }, ct);
        await using var grantLifetime = grant.ConfigureAwait(false);
        _proxy.BeginCall(new Dictionary<string, System.Net.IPAddress[]>(), grant.Token, ["api.mwmbl.org"]);
        try
        {
            var response = await _session.CallAsync(name, supplied, grant.Token, allowReadRecovery: true, authorize: token =>
            {
                token.ThrowIfCancellationRequested();
                _policy.RequireNetwork();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await grant.StopAsync().ConfigureAwait(false);
            _policy.RequireNetwork();
            var result = JsonSerializer.SerializeToNode(response, AssistantJsonContext.Default.AiMcpCallResult)!.AsObject();
            var search = new AiSearchObservation(decoded, DateTimeOffset.UtcNow.ToString("O"),
                response.Data.Content.Any(content => content.Text?.Contains("... [truncated]", StringComparison.Ordinal) == true),
                maximum, [.. _proxy.Failures]);
            result["search"] = JsonSerializer.SerializeToNode(search, AiToolObservationJsonContext.Default.AiSearchObservation);
            await emit("network_result", invocation, new JsonObject
            {
                ["source"] = "rodel_search",
                ["name"] = "doh_fetch",
                ["status"] = response.Status,
                ["search"] = JsonSerializer.SerializeToNode(search, AiToolObservationJsonContext.Default.AiSearchObservation)
            }).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && grant.Revocation is not null)
        {
            throw new LauncherOperationException("AI_NETWORK_ACCESS_REVOKED", "Search permission changed. Its pending transport was canceled and source data was withheld.");
        }
        finally
        {
            await grant.StopAsync().ConfigureAwait(false);
            await _proxy.EndCallAsync().ConfigureAwait(false);
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false); }
        finally { if (_proxy is not null) await _proxy.DisposeAsync().ConfigureAwait(false); }
    }
}