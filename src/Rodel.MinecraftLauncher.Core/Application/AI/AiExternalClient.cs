// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiExternalClient
{
    private const int OutputLimit = 64000;

    /// <summary>Gets the selected external client identifier.</summary>
    public string Client { get; }
    /// <summary>Gets the resolved client executable, or null for configuration export only.</summary>
    public string? Executable { get; }
    /// <summary>Gets the explicit user configuration root owned by the external client.</summary>
    public string ConfigRoot { get; }
    /// <summary>Gets the selected user skill directory; product file ownership is tracked separately.</summary>
    public string SkillsRoot { get; }
    private readonly bool _customConfigRoot;
    internal bool CanRegister => Executable is not null || (Client switch
    {
        "copilot" => File.Exists(CopilotConfigFile) || File.Exists(Path.Combine(ConfigRoot, "config.json")) || HasSelectedConfigRoot,
        "deepseek-harness" => File.Exists(HarnessConfigFile) || Directory.Exists(Path.Combine(ConfigRoot, "profiles")) || HasSelectedConfigRoot,
        _ => false
    });
    private bool HasSelectedConfigRoot => _customConfigRoot && Directory.Exists(ConfigRoot);
    private bool UsesConfigFile => Client is "copilot" or "deepseek-harness";
    /// <summary>Gets the actual client version observed during inspection.</summary>
    public string? Version { get; private set; }

    private string ClaudeConfigFile => Path.Combine(
        _customConfigRoot ? ConfigRoot : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude.json");
    private string CopilotConfigFile => Path.Combine(ConfigRoot, "mcp-config.json");
    private string HarnessConfigFile => Path.Combine(ConfigRoot, "cordis.patch.yml");

    /// <summary>Resolves client paths and environment overrides without installing or registering anything.</summary>
    public AiExternalClient(AiClientIntegrationRequest request)
    {
        if (request.Client is not ("codex" or "claude" or "copilot" or "deepseek-harness"))
            throw new ArgumentException("Select codex, claude, copilot or deepseek-harness.");
        Client = request.Client;
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var (variable, directory, executableName) = Client switch
        {
            "codex" => ("CODEX_HOME", ".codex", "codex.exe"),
            "claude" => ("CLAUDE_CONFIG_DIR", ".claude", "claude.exe"),
            "copilot" => ("COPILOT_HOME", ".copilot", "copilot.exe"),
            _ => ("DSH_HOME", ".dsh", "dsh.exe")
        };
        var configured = request.ClientConfigRoot ?? Environment.GetEnvironmentVariable(variable);
        _customConfigRoot = !string.IsNullOrWhiteSpace(configured);
        ConfigRoot = Absolute(string.IsNullOrWhiteSpace(configured) ? Path.Combine(home, directory) : configured);
        SkillsRoot = Absolute(request.SkillsRoot ?? (Client == "codex" ? Path.Combine(home, ".agents", "skills") : Path.Combine(ConfigRoot, "skills")));
        Executable = request.ClientExecutable is { } explicitPath ? Absolute(explicitPath) : FindExecutable(executableName);
        if (Executable is not null && (!File.Exists(Executable) || !Executable.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("Select an existing native client executable, or leave it absent for skills/config export.");
    }

    private static string Absolute(string path)
        => Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : throw new ArgumentException("Client paths must be absolute.");

    private static string? FindExecutable(string name)
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            if (!Path.IsPathFullyQualified(directory)) continue;
            var candidate = Path.Combine(directory, name);
            if (File.Exists(candidate)) return candidate;
        }

        return null;
    }

    /// <summary>Observes the product MCP registration, treating a missing configuration root as a fresh installation.</summary>
    public async Task<JsonObject?> ReadEntryAsync(CancellationToken ct)
    {
        if (Client == "copilot")
            return File.Exists(CopilotConfigFile) ? ReadJsonEntry(JsonFiles.ReadObject(CopilotConfigFile)) : null;
        if (Client == "deepseek-harness") return AiDeepSeekMcpConfig.Read(HarnessConfigFile);
        if (Executable is null) return null;
        Version ??= (await RunAsync(["--version"], ct).ConfigureAwait(false)).Trim();
        if (!Directory.Exists(ConfigRoot)) return null;
        if (Client == "claude")
        {
            var file = ClaudeConfigFile;
            if (!File.Exists(file)) return null;
            var config = JsonFiles.ReadObject(file);
            return ReadJsonEntry(config);
        }

        var output = await RunAsync(["mcp", "list", "--json"], ct).ConfigureAwait(false);
        var entries = JsonNode.Parse(output)?.AsArray() ?? throw new InvalidDataException("The client returned no MCP JSON catalog.");
        var entry = entries.OfType<JsonObject>().SingleOrDefault(item => item["name"]?.ToString() == "rodel-minecraft")?.DeepClone().AsObject();
        entry?.Remove("auth_status");
        return entry;
    }

    /// <summary>Creates the configuration directory when needed and writes the product registration through the selected client.</summary>
    public async Task RegisterAsync(JsonObject? previous, string launcher, string root, CancellationToken ct)
    {
        Directory.CreateDirectory(ConfigRoot);
        switch (Client)
        {
            case "codex":
                await WriteCodexEntryAsync(new JsonObject { ["command"] = launcher, ["args"] = McpArgumentJson(root) }, ct).ConfigureAwait(false);
                break;
            case "claude" when previous is not null:
                await ReplaceClaudeEntryAsync(previous, launcher, root, ct).ConfigureAwait(false);
                break;
            case "claude":
                await AddClaudeEntryAsync(launcher, root, ct).ConfigureAwait(false);
                break;
            case "copilot":
                await AiJsonMcpConfig.EditAsync(CopilotConfigFile, previous, ExpectedEntry(launcher, root), ct).ConfigureAwait(false);
                break;
            default:
                await AiDeepSeekMcpConfig.EditAsync(HarnessConfigFile, previous, ExpectedEntry(launcher, root), ct).ConfigureAwait(false);
                break;
        }
    }

    private async Task ReplaceClaudeEntryAsync(JsonObject previous, string launcher, string root, CancellationToken ct)
    {
        var staging = Directory.CreateTempSubdirectory("Rodel.Claude.Mcp.");
        JsonObject candidate;
        try
        {
            await AddClaudeEntryAsync(launcher, root, ct, staging.FullName).ConfigureAwait(false);
            candidate = ReadJsonEntry(JsonFiles.ReadObject(Path.Combine(staging.FullName, ".claude.json")))
                ?? throw new IOException("Claude did not write the candidate MCP entry.");
            if (!Matches(candidate, launcher, root))
                throw new IOException("Claude did not retain the planned command and data root in the candidate MCP entry.");
        }
        finally
        {
            staging.Delete(recursive: true);
        }

        await AiJsonMcpConfig.EditAsync(ClaudeConfigFile, previous, candidate, ct).ConfigureAwait(false);
    }

    private Task<string> AddClaudeEntryAsync(string launcher, string root, CancellationToken ct, string? stagingRoot = null)
        => RunAsync(["mcp", "add", "--scope", "user", "--transport", "stdio", "rodel-minecraft", "--", launcher, .. McpArguments(root)],
            ct, claudeConfigRoot: stagingRoot);

    private static JsonObject? ReadJsonEntry(JsonObject config)
        => config["mcpServers"]?["rodel-minecraft"]?.DeepClone().AsObject();

    internal static string[] McpArguments(string root) => ["--data-dir", root, "mcp", "serve", "--stdio", "--tool-profile", "discovery"];

    private static JsonArray McpArgumentJson(string root) => new([.. McpArguments(root).Select(argument => (JsonNode?)JsonValue.Create(argument))]);

    /// <summary>Removes the product MCP entry through the selected client's user-scope configuration command.</summary>
    public Task RemoveAsync(JsonObject? previous, CancellationToken ct) => Client switch
    {
        "codex" => WriteCodexEntryAsync(null, ct),
        "claude" => RunAsync(["mcp", "remove", "--scope", "user", "rodel-minecraft"], ct),
        "copilot" => AiJsonMcpConfig.EditAsync(CopilotConfigFile, previous, null, ct),
        _ => AiDeepSeekMcpConfig.EditAsync(HarnessConfigFile, previous, null, ct)
    };

    /// <summary>Compares the actual executable, transport and full argument binding with the intended standalone CLI.</summary>
    public bool Matches(JsonObject? entry, string launcher, string root)
    {
        if (entry is null) return false;
        if (UsesConfigFile) return JsonNode.DeepEquals(entry, ExpectedEntry(launcher, root));
        var transport = Client == "codex" ? entry["transport"] : entry;
        return transport?["type"]?.ToString() == "stdio"
            && string.Equals(transport["command"]?.ToString(), launcher, StringComparison.OrdinalIgnoreCase)
            && JsonNode.DeepEquals(transport["args"], McpArgumentJson(root));
    }

    /// <summary>Builds the exact client-specific entry used for frozen review and recovery matching.</summary>
    public JsonObject ExpectedEntry(string launcher, string root)
    {
        if (Client == "deepseek-harness") return AiDeepSeekMcpConfig.Entry(launcher, McpArgumentJson(root));
        if (Client == "copilot")
        {
            return new JsonObject
            {
                ["type"] = "local",
                ["command"] = launcher,
                ["args"] = McpArgumentJson(root),
                ["tools"] = new JsonArray(JsonValue.Create("*"))
            };
        }

        var transport = new JsonObject
        {
            ["type"] = "stdio",
            ["command"] = launcher,
            ["args"] = McpArgumentJson(root)
        };
        if (Client == "claude")
        {
            transport["env"] = new JsonObject();
            return transport;
        }

        transport["env"] = null;
        transport["env_vars"] = new JsonArray();
        transport["cwd"] = null;
        return new JsonObject
        {
            ["name"] = "rodel-minecraft",
            ["enabled"] = true,
            ["disabled_reason"] = null,
            ["transport"] = transport,
            ["startup_timeout_sec"] = null,
            ["tool_timeout_sec"] = null
        };
    }

    private Task<string> WriteCodexEntryAsync(JsonObject? entry, CancellationToken ct)
        => RunAsync(["app-server", "--listen", "stdio://"], ct, async (process, token) =>
        {
            await CallCodexAsync(process, 1, "initialize", new JsonObject
            {
                ["clientInfo"] = new JsonObject { ["name"] = "rodel-minecraft", ["version"] = "1" }
            }, token).ConfigureAwait(false);
            var initialized = new JsonObject { ["jsonrpc"] = "2.0", ["method"] = "initialized" };
            await process.StandardInput.WriteLineAsync(initialized.ToJsonString().AsMemory(), token).ConfigureAwait(false);

            // Codex's mcp add/remove rewrites every server. A null value clears this one key.
            var result = await CallCodexAsync(process, 2, "config/value/write", new JsonObject
            {
                ["keyPath"] = "mcp_servers.rodel-minecraft",
                ["mergeStrategy"] = "replace",
                ["value"] = entry
            }, token).ConfigureAwait(false);
            if (result["status"]?.ToString() != "ok")
                throw new LauncherOperationException("AI_CODEX_CONFIG_WRITE_FAILED", "Codex did not make the requested MCP registration effective.");
        });

    private static async Task<JsonObject> CallCodexAsync(Process process, int id, string method, JsonObject parameters, CancellationToken ct)
    {
        var request = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = parameters };
        await process.StandardInput.WriteLineAsync(request.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
        var remaining = OutputLimit;
        while (await process.StandardOutput.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
        {
            remaining -= line.Length + 1;
            if (remaining < 0)
                throw new LauncherOperationException("AI_CODEX_CONFIG_WRITE_FAILED", "Codex configuration responses exceeded the inspection limit.");
            var response = JsonNode.Parse(line) as JsonObject;
            if (response?["id"] is not JsonValue value || !value.TryGetValue<int>(out var responseId) || responseId != id)
                continue;
            if (response["result"] is JsonObject result) return result;
            break;
        }

        throw new LauncherOperationException("AI_CODEX_CONFIG_WRITE_FAILED", "Codex did not accept the scoped MCP configuration update. Select a Codex executable with configuration write support.");
    }

    private async Task<string> RunAsync(string[] arguments, CancellationToken ct, Func<Process, CancellationToken, Task>? exchange = null,
        string? claudeConfigRoot = null)
    {
        if (Executable is null) throw new LauncherOperationException("AI_EXTERNAL_CLIENT_MISSING", "The selected client is absent. Skills and configuration export remain available.");
        var working = Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher", "client-management");
        Directory.CreateDirectory(working);
        using var pinned = AiScopedFiles.OpenDirectory(working);
        var start = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = working,
            RedirectStandardInput = exchange is not null,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        if (exchange is not null) start.StandardInputEncoding = new UTF8Encoding(false);
        start.Environment.Clear();
        foreach (var name in new[] { "SystemRoot", "WINDIR", "USERPROFILE", "LOCALAPPDATA", "APPDATA", "TEMP", "TMP", "PATH", "PATHEXT" })
            if (Environment.GetEnvironmentVariable(name) is { } value) start.Environment[name] = value;
        if (Client == "codex") start.Environment["CODEX_HOME"] = ConfigRoot;
        else if (Client == "claude" && (claudeConfigRoot is not null || _customConfigRoot))
            start.Environment["CLAUDE_CONFIG_DIR"] = claudeConfigRoot ?? ConfigRoot;
        start.Environment["CLAUDE_CODE_DISABLE_NONESSENTIAL_TRAFFIC"] = "1";
        start.Environment["DISABLE_TELEMETRY"] = "1";
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var job = new AiChildProcessJob(independentLauncherChildren: false);
        using var process = Process.Start(start) ?? throw new IOException("The selected client management process did not start.");
        try { job.Assign(process); }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            throw;
        }

        var error = ReadBoundedAsync(process.StandardError);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        string stdout;
        try
        {
            if (exchange is not null)
            {
                await exchange(process, deadline.Token).ConfigureAwait(false);
                process.StandardInput.Close();
            }

            var output = ReadBoundedAsync(process.StandardOutput);
            await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false);
            stdout = await output.ConfigureAwait(false);
        }
        catch
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        var stderr = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new LauncherOperationException("AI_EXTERNAL_CLIENT_COMMAND_FAILED", "The client management command failed. Existing configuration was not treated as successfully updated.",
                new JsonObject { ["client"] = Client, ["exitCode"] = process.ExitCode, ["detail"] = SecretScrubber.Scrub(stderr, null) });
        }

        return stdout;
    }

    private static async Task<string> ReadBoundedAsync(StreamReader reader)
    {
        var result = new StringBuilder();
        var buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            var keep = Math.Min(count, OutputLimit - result.Length);
            if (keep > 0) result.Append(buffer, 0, keep);
        }

        return result.Length == OutputLimit ? throw new InvalidDataException("Client command output exceeded its bounded inspection size.") : result.ToString();
    }
}