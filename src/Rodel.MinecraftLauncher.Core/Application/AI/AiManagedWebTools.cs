// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiManagedWebTools(LauncherPaths paths, JsonObject turn, AiManagedComponents components,
    Func<string, string?, JsonObject, Task> emit) : IAsyncDisposable
{
    private readonly AiToolPolicy _policy = new(paths, turn);
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);
    private AiWebEgressProxy? _proxy;
    private AiManagedMcpSession? _session;

    public async Task<IReadOnlyList<AIFunctionDeclaration>> ConnectAsync(CancellationToken ct)
    {
        if (turn["networkAccessEnabled"]?.GetValue<bool>() != true) return [];
        try
        {
            _policy.RequireNetwork();
            var (component, executable) = await components.OpenAsync("rodel_web", ct).ConfigureAwait(false);
            _proxy = new AiWebEgressProxy();
            var registration = new AiMcpRegistration
            {
                Id = component.Id,
                DisplayName = "Rodel managed web",
                ExecutablePath = executable,
                WorkingDirectory = Path.GetDirectoryName(executable),
                Arguments = [],
                Environment = new Dictionary<string, string?>
                {
                    ["MCP_TRANSPORT"] = "stdio",
                    ["MAX_QUERIES"] = "3",
                    ["MAX_RESULTS"] = "10",
                    ["DEFAULT_TIMEOUT_MS"] = "15000",
                    ["MAX_TIMEOUT_MS"] = "25000",
                    ["MAX_DOCUMENT_CHARS"] = "20000"
                }
            };
            _session = await AiManagedMcpSession.ConnectAsync(registration, emit, privateEnvironment: new Dictionary<string, string>
            {
                ["PROXY_SCHEME"] = "http",
                ["PROXY_HOST"] = "127.0.0.1",
                ["PROXY_PORT"] = _proxy.Port.ToString(System.Globalization.CultureInfo.InvariantCulture),
                ["PROXY_LOGIN"] = _proxy.Username,
                ["PROXY_PASSWORD"] = _proxy.Password
            }, privateSecrets: [_proxy.Password], ct: ct).ConfigureAwait(false);
            var tools = new List<AIFunctionDeclaration>();
            foreach (var tool in _session.Tools)
            {
                var actual = _session.ActualName(tool.Name);
                if (actual != "web_scrape") continue;
                _names.Add(tool.Name, actual);
                tools.Add(AIFunctionFactory.CreateDeclaration(tool.Name,
                    tool.Description + " Product-managed web-page retrieval, separate from the Mwmbl public search API reader. Private/local URLs need one-call user confirmation; redirects cannot enlarge that permission. Respect robots.txt, source and per-item failures. Prefer real launcher content catalogs for Minecraft content and official source pages for version claims.",
                    tool.JsonSchema));
            }

            await emit("managed_mcp_ready", "managed:web", new JsonObject
            {
                ["id"] = component.Id,
                ["version"] = component.Version,
                ["revision"] = component.Revision,
                ["purpose"] = "web_page_reading",
                ["publicDnsProvider"] = "Cloudflare DNS",
                ["toolCount"] = tools.Count,
                ["processId"] = _session.ProcessId,
                ["userSearchKeyRequired"] = false
            }).ConfigureAwait(false);
            return tools;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await emit("managed_mcp_unavailable", "managed:web", new JsonObject
            {
                ["id"] = "rodel_web",
                ["ordinaryConversationAvailable"] = true,
                ["recovery"] = "Retry managed component startup or repair/update the package; no user MCP or search-key setup is required.",
                ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
            }).ConfigureAwait(false);
            return [];
        }
    }

    public bool Contains(string name) => _names.ContainsKey(name);

    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, string invocation, CancellationToken ct)
    {
        if (_session is null || _proxy is null || !_names.TryGetValue(name, out var actual))
            throw new LauncherOperationException("AI_MANAGED_MCP_UNAVAILABLE", "The managed web tool is not connected in this turn.");
        _policy.RequireNetwork();
        var privateEndpoints = new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase);
        if (actual == "web_scrape")
        {
            arguments["robots_txt"] = true;
            arguments["max_chars"] = Math.Min(arguments["max_chars"]?.GetValue<int>() ?? 20000, 20000);
            var review = await ReviewAddressesAsync(arguments, privateEndpoints, ct).ConfigureAwait(false);
            if (privateEndpoints.Count > 0)
            {
                await new AiToolApprovals(paths).WaitAsync(turn, invocation, name, arguments, review,
                    token => ReviewAddressesAsync(arguments, new Dictionary<string, IPAddress[]>(StringComparer.OrdinalIgnoreCase), token),
                    token =>
                    {
                        token.ThrowIfCancellationRequested();
                        _policy.RequireNetwork();
                        return Task.CompletedTask;
                    }, emit, ct).ConfigureAwait(false);
            }
        }

        var grant = new AiCallGrant(token =>
        {
            token.ThrowIfCancellationRequested();
            _policy.RequireNetwork();
            return Task.CompletedTask;
        }, ct);
        await using var grantLifetime = grant.ConfigureAwait(false);
        _proxy.BeginCall(privateEndpoints, grant.Token);
        try
        {
            var response = await _session.CallAsync(name, arguments, grant.Token, allowReadRecovery: true, authorize: token =>
            {
                token.ThrowIfCancellationRequested();
                _policy.RequireNetwork();
                return Task.CompletedTask;
            }).ConfigureAwait(false);
            await grant.StopAsync().ConfigureAwait(false);
            _policy.RequireNetwork();
            var result = JsonSerializer.SerializeToNode(response, AssistantJsonContext.Default.AiMcpCallResult)!.AsObject();
            var web = new AiWebObservation(actual, DateTimeOffset.UtcNow.ToString("O"), [.. _proxy.Failures]);
            result["managedWeb"] = JsonSerializer.SerializeToNode(web, AiToolObservationJsonContext.Default.AiWebObservation);
            await emit("network_result", invocation, new JsonObject
            {
                ["source"] = "rodel_web",
                ["name"] = actual,
                ["status"] = response.Status,
                ["privateEndpointPermission"] = privateEndpoints.Count > 0,
                ["transportFailures"] = new JsonArray([.. web.TransportFailures.Select(value => (JsonNode?)JsonValue.Create(value))])
            }).ConfigureAwait(false);
            return result;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && grant.Revocation is not null)
        {
            throw new LauncherOperationException("AI_NETWORK_ACCESS_REVOKED", "Network permission changed. The pending request was canceled and its data was not supplied to the model.");
        }
        finally
        {
            await grant.StopAsync().ConfigureAwait(false);
            await _proxy.EndCallAsync().ConfigureAwait(false);
        }
    }

    private async Task<AiToolReview> ReviewAddressesAsync(JsonObject arguments, Dictionary<string, IPAddress[]> approved, CancellationToken ct)
    {
        _policy.RequireNetwork();
        if (arguments["urls"] is not JsonArray urls || urls.Count is < 1 or > 3)
            throw new ArgumentException("Provide one through three explicit HTTP(S) URLs.");
        var targets = new List<AiWebAccessTarget>();
        foreach (var value in urls)
        {
            var text = value?.GetValue<string>() ?? string.Empty;
            if (text.Length > 2048 || !Uri.TryCreate(text, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
                || !string.IsNullOrEmpty(uri.UserInfo) || !string.IsNullOrEmpty(uri.Fragment))
            {
                throw new ArgumentException("Use bounded HTTP(S) URLs without embedded credentials or fragments.");
            }

            var addresses = (await _proxy!.ResolveAsync(uri.DnsSafeHost, ct).ConfigureAwait(false))
                .OrderBy(address => address.ToString(), StringComparer.Ordinal).ToArray();
            if (addresses.Length == 0) throw new HttpRequestException("The requested host did not resolve.");
            if (AiWebEgressProxy.PrivateName(uri.DnsSafeHost) || addresses.Any(AiWebEgressProxy.IsPrivate))
            {
                approved[AiWebEgressProxy.Endpoint(uri.DnsSafeHost, uri.Port)] = addresses;
                targets.Add(new(uri.AbsoluteUri, uri.DnsSafeHost, uri.Port, [.. addresses.Select(address => address.ToString())]));
            }
        }

        return new AiWebAccessReview([.. targets]);
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false); }
        finally { if (_proxy is not null) await _proxy.DisposeAsync().ConfigureAwait(false); }
    }
}