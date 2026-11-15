// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>A session-owned child with the official MCP protocol engine and an isolated environment.</summary>
internal sealed class AiMcpSession : IAsyncDisposable
{
    private readonly Process _process;
    private readonly Task _drainError;
    private readonly AiChildProcessJob _job;
    private McpClient? _client;
    private readonly List<string> _secrets = [];
    /// <summary>Gets the frozen registration used to start this session's child.</summary>
    public AiMcpRegistration Registration { get; }
    /// <summary>Gets the supported, source-qualified tools discovered during connection.</summary>
    public List<AIFunctionDeclaration> Tools { get; } = [];
    /// <summary>Gets catalog entries rejected during bounded discovery.</summary>
    public List<AiMcpCatalogWarning> Warnings { get; } = [];
    /// <summary>Gets whether the owned MCP child has exited.</summary>
    public bool HasExited => _process.HasExited;
    /// <summary>Gets the owned MCP child's process identifier.</summary>
    public int ProcessId => _process.Id;
    private readonly Dictionary<string, string> _names = new(StringComparer.Ordinal);

    private AiMcpSession(AiMcpRegistration registration, bool launcherChild, IReadOnlyDictionary<string, string>? privateEnvironment,
        IReadOnlyList<string>? privateSecrets)
    {
        Registration = registration with
        {
            Arguments = [.. registration.Arguments],
            Environment = registration.Environment is null ? null : new(registration.Environment),
            CredentialEnvironment = registration.CredentialEnvironment is null ? null : new(registration.CredentialEnvironment)
        };
        var start = new ProcessStartInfo(Registration.ExecutablePath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = new UTF8Encoding(false),
            StandardErrorEncoding = new UTF8Encoding(false),
            WorkingDirectory = Registration.WorkingDirectory ?? Path.GetDirectoryName(Registration.ExecutablePath)!
        };
        foreach (var argument in Registration.Arguments) start.ArgumentList.Add(argument);
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "TEMP", "TMP", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "PATH", "PATHEXT" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        if (Registration.Environment is { } environment)
        {
            foreach (var pair in environment)
            {
                if (pair.Value is null) start.Environment.Remove(pair.Key);
                else start.Environment[pair.Key] = pair.Value;
            }
        }

        if (Registration.CredentialEnvironment is { } credentials)
        {
            foreach (var pair in credentials)
            {
                var reference = pair.Value;
                if (!reference.StartsWith("env:", StringComparison.Ordinal))
                    throw new AiProtocolException("AI_MCP_CREDENTIAL_INVALID", "An MCP credential must reference its explicitly selected environment variable.");
                var value = Environment.GetEnvironmentVariable(reference[4..]);
                if (string.IsNullOrEmpty(value)) throw new AiProtocolException("AI_MCP_CREDENTIAL_UNAVAILABLE", "An explicitly configured MCP credential variable is missing.");
                _secrets.Add(value);
                start.Environment[pair.Key] = value;
            }
        }

        if (privateEnvironment is not null)
            foreach (var pair in privateEnvironment) start.Environment[pair.Key] = pair.Value;
        _secrets.AddRange(privateSecrets ?? []);
        _job = new AiChildProcessJob(independentLauncherChildren: launcherChild);
        Process? process = null;
        try
        {
            process = Process.Start(start) ?? throw new AiProtocolException("AI_MCP_START_FAILED", "The configured MCP child could not be started.");
            _job.Assign(process);
            _process = process;
        }
        catch
        {
            _job.Dispose();
            if (process is not null)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.Dispose();
            }

            throw;
        }
        // Drain continuously without retaining or logging credentials printed by an untrusted child.
        _drainError = DrainAsync(_process.StandardError);
    }

    /// <summary>Starts an isolated child, initializes MCP and freezes its bounded tool catalog; failure disposes the child.</summary>
    public static async Task<AiMcpSession> ConnectAsync(AiMcpRegistration registration, CancellationToken ct, bool launcherChild = false,
        IReadOnlyDictionary<string, string>? privateEnvironment = null, IReadOnlyList<string>? privateSecrets = null)
    {
        var session = new AiMcpSession(registration, launcherChild, privateEnvironment, privateSecrets);
        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            session._client = await McpClient.CreateAsync(
                new StreamClientTransport(session._process.StandardInput.BaseStream, new BoundedMcpStream(session._process.StandardOutput.BaseStream)),
                new McpClientOptions
                {
                    ClientInfo = new Implementation { Name = "rodel-minecraft-assistant", Version = "1" },
                    ProtocolVersion = "2025-06-18",
                    InitializationTimeout = TimeSpan.FromSeconds(15)
                }, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (session._client.ServerCapabilities.Tools is null)
                throw new AiProtocolException("AI_MCP_TOOLS_UNAVAILABLE", "This MCP server does not advertise tools.");
            string? cursor = null;
            var discovered = 0;
            var cursors = new HashSet<string>(StringComparer.Ordinal);
            do
            {
                var page = await session._client.ListToolsAsync(new ListToolsRequestParams { Cursor = cursor }, deadline.Token).ConfigureAwait(false);
                foreach (var tool in page.Tools)
                {
                    if (discovered >= 100) throw new AiProtocolException("AI_MCP_CATALOG_TOO_LARGE", "One MCP server may expose at most 100 tools per snapshot.");
                    var ordinal = discovered++;
                    if (tool.InputSchema.ValueKind != JsonValueKind.Object)
                    {
                        session.Warnings.Add(new("AI_MCP_SCHEMA_UNSUPPORTED", ordinal));
                        continue;
                    }

                    if (JsonNode.Parse(tool.InputSchema.GetRawText()) is not JsonObject schema || schema.ToJsonString().Length > 32000)
                    {
                        session.Warnings.Add(new("AI_MCP_SCHEMA_UNSUPPORTED", ordinal));
                        continue;
                    }

                    if (schema["type"] is null || (schema["type"] is JsonArray types && types.Any(type => type?.ToString() == "object")))
                        schema["type"] = "object";
                    if (schema["type"]?.ToString() != "object")
                    {
                        session.Warnings.Add(new("AI_MCP_SCHEMA_UNSUPPORTED", ordinal));
                        continue;
                    }

                    var description = "MCP source " + session.Registration.DisplayName + "; actual tool " + tool.Name + ". " + tool.Description;
                    if (session._secrets.Any(secret => SecretScrubber.ContainsSecret(schema, secret)
                        || description.Contains(secret, StringComparison.Ordinal) || tool.Name.Contains(secret, StringComparison.Ordinal)))
                    {
                        session.Warnings.Add(new("AI_MCP_CREDENTIAL_IN_CATALOG", ordinal));
                        continue;
                    }

                    var name = "mcp_" + session.Registration.Id.Replace("-", "_", StringComparison.Ordinal) + "_" + ordinal;
                    session._names.Add(name, tool.Name);
                    using var normalized = JsonDocument.Parse(schema.ToJsonString());
                    tool.InputSchema = normalized.RootElement.Clone();
                    session.Tools.Add(new McpClientTool(session._client, tool)
                        .WithName(name)
                        .WithDescription(description)
                        .AsDeclarationOnly());
                }

                cursor = page.NextCursor;
                if (cursor is not null && !cursors.Add(cursor))
                    throw new AiProtocolException("AI_MCP_CURSOR_REPEATED", "MCP tool discovery returned a repeated cursor.");
            } while (cursor is not null);
            return session;
        }
        catch
        {
            await session.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>Checks whether a model-visible tool belongs to this frozen catalog.</summary>
    public bool Contains(string name) => _names.ContainsKey(name);
    /// <summary>Resolves a catalog tool's source-qualified name to its server-side name.</summary>
    public string ActualName(string name) => _names[name];
    /// <summary>Invokes a frozen tool once under the caller's cancellation policy, without a second tool deadline or mutation replay.</summary>
    public async Task<AiMcpCallResult> CallAsync(string name, JsonObject arguments, CancellationToken ct, string? clientRequestId = null)
    {
        if (_client is null || !_names.TryGetValue(name, out var actual)) throw new AiProtocolException("AI_MCP_TOOL_STALE", "This tool is not in the frozen MCP catalog.");
        var supplied = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in arguments)
        {
            using var document = JsonDocument.Parse(pair.Value?.ToJsonString() ?? "null");
            supplied[pair.Key] = document.RootElement.Clone();
        }

        CallToolResult result;
        try
        {
            result = await _client.CallToolAsync(new CallToolRequestParams
            {
                Name = actual,
                Arguments = supplied,
                Meta = clientRequestId is null ? null : new JsonObject { ["rodel.clientRequestId"] = clientRequestId }
            }, ct).ConfigureAwait(false);
        }
        catch (IOException error)
        {
            var detail = new JsonObject { ["sourceId"] = Registration.Id, ["actualName"] = actual, ["cause"] = error.Message };
            foreach (var secret in _secrets) SecretScrubber.Scrub(detail, secret);
            throw new AiProtocolException("AI_MCP_TRANSPORT_FAILED",
                "The MCP transport failed; the call may have been sent. This is not evidence that it had no effects.", detail);
        }

        var content = new List<AiMcpContent>();
        foreach (var block in result.Content)
        {
            if (block is TextContentBlock text)
            {
                content.Add(new("text") { Text = text.Text });
            }
            else if (block is ImageContentBlock image)
            {
                byte[] bytes;
                try { bytes = image.DecodedData.ToArray(); }
                catch (FormatException)
                {
                    throw new AiProtocolException("AI_MCP_IMAGE_INVALID", "The MCP image block does not contain valid base64 image bytes.");
                }

                if (bytes.Length == 0 || image.MimeType is not { Length: > 0 } mediaType
                    || !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase))
                {
                    throw new AiProtocolException("AI_MCP_IMAGE_INVALID", "The MCP image block has no image bytes or image media type.");
                }

                content.Add(new("image") { Data = bytes, MediaType = mediaType, ByteLength = bytes.Length });
            }
            else
            {
                content.Add(new(block.Type) { Unavailable = "Only text and image MCP results are consumed by this assistant." });
            }
        }

        var output = new AiMcpCallResult(result.IsError == true ? "failed" : "succeeded",
            new(Registration.Id, actual, [.. content], result.StructuredContent is { } structured ? JsonNode.Parse(structured.GetRawText()) : null));
        var textOnly = output with { Data = output.Data with { Content = [.. content.Select(block => block with { Data = null })] } };
        if (JsonSerializer.Serialize(textOnly, AssistantJsonContext.Default.AiMcpCallResult).Length > 96000)
            throw new AiProtocolException("AI_MCP_RESULT_TOO_LARGE", "The MCP result exceeds this call's consumption budget. Use the tool's pagination parameters.");
        foreach (var secret in _secrets)
        {
            if (output.Data.StructuredContent is { } data) SecretScrubber.Scrub(data, secret);
            output = output with
            {
                Data = output.Data with
                {
                    ToolName = SecretScrubber.Scrub(output.Data.ToolName, secret),
                    Content = [.. output.Data.Content.Select(block => block with
                    {
                        Type = SecretScrubber.Scrub(block.Type, secret),
                        Text = block.Text is null ? null : SecretScrubber.Scrub(block.Text, secret)
                    })]
                }
            };
        }

        return output;
    }

    /// <summary>Observes this ready session while scrubbing secrets from external descriptions and schemas.</summary>
    public AiMcpDescription Describe()
    {
        string Clean(string text)
        {
            foreach (var secret in _secrets) text = SecretScrubber.Scrub(text, secret);
            return text;
        }

        JsonNode CleanNode(JsonNode node)
        {
            var clone = node.DeepClone();
            if (clone is JsonValue value && value.TryGetValue<string>(out var text)) return JsonValue.Create(Clean(text))!;
            foreach (var secret in _secrets) SecretScrubber.Scrub(clone, secret);
            return clone;
        }

        return new()
        {
            Id = Registration.Id,
            DisplayName = Clean(Registration.DisplayName),
            Status = "ready",
            ProcessId = _process.Id,
            ServerName = Clean(_client!.ServerInfo.Name),
            ServerVersion = Clean(_client.ServerInfo.Version),
            Tools = [.. Tools.Select(tool => new AiMcpToolDescription
            {
                Name = Clean(tool.Name),
                ActualName = Clean(ActualName(tool.Name)),
                InputSchema = CleanNode(AiToolDeclarations.Schema(tool)).AsObject()
            })],
            Warnings = [.. Warnings]
        };
    }

    /// <summary>Closes the client and terminates only this session's owned process tree, then finishes draining stderr.</summary>
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (_client is not null) await _client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
        }
        finally
        {
            _job.Dispose();
            _process.StandardInput.BaseStream.Close();
            if (!_process.HasExited)
            {
                try { await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false); }
                catch (TimeoutException)
                {
                    _process.Kill(entireProcessTree: true);
                    await _process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
                }
            }

            await _drainError.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            _process.Dispose();
        }
    }

    private static async Task DrainAsync(StreamReader reader)
    {
        var buffer = new char[2048];
        while (await reader.ReadAsync(buffer).ConfigureAwait(false) != 0) { }
    }

}