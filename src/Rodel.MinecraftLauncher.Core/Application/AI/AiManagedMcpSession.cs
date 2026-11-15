// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiManagedMcpSession : IAsyncDisposable
{
    private readonly AiMcpRegistration _registration;
    private readonly IReadOnlyDictionary<string, string>? _environment;
    private readonly IReadOnlyList<string>? _secrets;
    private readonly Func<string, string?, JsonObject, Task> _emit;
    private readonly Dictionary<string, string> _actual = new(StringComparer.Ordinal);
    private Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private AiMcpSession? _session;
    public IReadOnlyList<AIFunctionDeclaration> Tools { get; private set; } = [];
    public int ProcessId => _session?.ProcessId
        ?? throw new InvalidOperationException("The managed MCP session is not connected.");

    private AiManagedMcpSession(AiMcpRegistration registration, Func<string, string?, JsonObject, Task> emit,
        IReadOnlyDictionary<string, string>? environment, IReadOnlyList<string>? secrets)
    {
        _registration = registration with
        {
            Arguments = [.. registration.Arguments],
            Environment = registration.Environment is null ? null : new(registration.Environment, StringComparer.OrdinalIgnoreCase),
            CredentialEnvironment = registration.CredentialEnvironment is null ? null : new(registration.CredentialEnvironment, StringComparer.OrdinalIgnoreCase)
        };
        _environment = environment is null ? null : new Dictionary<string, string>(environment, StringComparer.OrdinalIgnoreCase);
        _secrets = secrets?.ToArray();
        _emit = emit;
    }

    public static async Task<AiManagedMcpSession> ConnectAsync(AiMcpRegistration registration,
        Func<string, string?, JsonObject, Task> emit, IReadOnlyDictionary<string, string>? privateEnvironment = null,
        IReadOnlyList<string>? privateSecrets = null, CancellationToken ct = default)
    {
        var managed = new AiManagedMcpSession(registration, emit, privateEnvironment, privateSecrets);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                managed._session = await managed.OpenAsync(ct).ConfigureAwait(false);
                managed.Tools = [.. managed._session.Tools];
                foreach (var tool in managed.Tools) managed._actual.Add(tool.Name, managed._session.ActualName(tool.Name));
                managed._current = managed._actual.Keys.ToDictionary(name => name, name => name, StringComparer.Ordinal);
                return managed;
            }
            catch (Exception error)
            {
                if (managed._session is not null)
                {
                    await managed.CloseAsync(managed._session).ConfigureAwait(false);
                    managed._session = null;
                    managed._actual.Clear();
                }

                if (attempt != 0 || ct.IsCancellationRequested) throw;
                await managed.ReconnectingAsync(error, "startup").ConfigureAwait(false);
            }
        }
    }

    public string ActualName(string name) => _actual[name];
    private Task<AiMcpSession> OpenAsync(CancellationToken ct)
        => AiMcpSession.ConnectAsync(_registration, ct, privateEnvironment: _environment, privateSecrets: _secrets);

    /// <summary>Returns the normalized MCP response while preserving open third-party structured content.</summary>
    public async Task<AiMcpCallResult> CallAsync(string name, JsonObject arguments, CancellationToken ct,
        bool allowReadRecovery = false, Func<CancellationToken, Task>? authorize = null)
    {
        for (var attempt = 0; ; attempt++)
        {
            var session = _session ?? throw new InvalidOperationException("The managed MCP session is unavailable.");
            try
            {
                return await session.CallAsync(_current[name], arguments, ct).ConfigureAwait(false);
            }
            catch (Exception error) when (attempt == 0 && allowReadRecovery && !ct.IsCancellationRequested && session.HasExited)
            {
                if (authorize is not null) await authorize(ct).ConfigureAwait(false);
                await ReconnectingAsync(error, "read").ConfigureAwait(false);
                _session = null;
                await CloseAsync(session).ConfigureAwait(false);
                var replacement = await OpenAsync(ct).ConfigureAwait(false);
                try
                {
                    var aliases = new Dictionary<string, string>(StringComparer.Ordinal);
                    if (replacement.Tools.Count != Tools.Count)
                        throw new LauncherOperationException("AI_MANAGED_MCP_CATALOG_CHANGED", "The managed tool catalog changed during recovery. Start a new turn; no stale read was dispatched.");
                    foreach (var frozen in Tools)
                    {
                        var current = replacement.Tools.SingleOrDefault(tool => replacement.ActualName(tool.Name) == _actual[frozen.Name]);
                        if (current is null || !JsonNode.DeepEquals(AiToolDeclarations.Schema(frozen), AiToolDeclarations.Schema(current)))
                            throw new LauncherOperationException("AI_MANAGED_MCP_CATALOG_CHANGED", "The managed tool identity or schema changed during recovery. No stale read was dispatched.");
                        aliases.Add(frozen.Name, current.Name);
                    }

                    if (authorize is not null) await authorize(ct).ConfigureAwait(false);
                    _current = aliases;
                    _session = replacement;
                    await _emit("managed_mcp_reconnected", "managed:" + _registration.Id, new JsonObject
                    {
                        ["id"] = _registration.Id,
                        ["processId"] = ProcessId,
                        ["catalogChanged"] = false,
                        ["readRetryPrepared"] = true,
                        ["mutationReplayed"] = false
                    }).ConfigureAwait(false);
                }
                catch
                {
                    _session = null;
                    await replacement.DisposeAsync().ConfigureAwait(false);
                    throw;
                }
            }
        }
    }

    private Task ReconnectingAsync(Exception error, string phase)
    {
        var detail = LauncherOperationException.ToResult(error)["error"]!.DeepClone().AsObject();
        foreach (var secret in _secrets ?? []) SecretScrubber.Scrub(detail, secret);
        return _emit("managed_mcp_reconnecting", "managed:" + _registration.Id, new JsonObject
        {
            ["id"] = _registration.Id,
            ["phase"] = phase,
            ["attempt"] = 1,
            ["mutationReplayed"] = false,
            ["error"] = detail
        });
    }

    public async ValueTask DisposeAsync()
    {
        var session = _session;
        _session = null;
        if (session is not null) await CloseAsync(session).ConfigureAwait(false);
    }

    private async Task CloseAsync(AiMcpSession session)
    {
        var alreadyExited = session.HasExited;
        try { await session.DisposeAsync().ConfigureAwait(false); }
        catch (IOException error) when (alreadyExited)
        {
            var detail = LauncherOperationException.ToResult(error)["error"]!.DeepClone().AsObject();
            foreach (var secret in _secrets ?? []) SecretScrubber.Scrub(detail, secret);
            await _emit("managed_mcp_cleanup_warning", "managed:" + _registration.Id, new JsonObject
            {
                ["id"] = _registration.Id,
                ["processAlreadyExited"] = true,
                ["error"] = detail
            }).ConfigureAwait(false);
        }
    }
}