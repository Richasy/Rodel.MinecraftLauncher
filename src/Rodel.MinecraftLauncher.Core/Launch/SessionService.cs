// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

public sealed partial class SessionService(LauncherPaths paths)
{
    private string FilePath(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Invalid session identifier.");
        return Path.Combine(paths.SessionsDirectory, id + ".json");
    }

    private JsonObject GetRecord(string id) => JsonFiles.ReadObject(FilePath(id));

    /// <summary>Reads one session, observes process identities and retains capture or crash-snapshot warnings.</summary>
    public SessionStatus Get(string id) => Get(id, refreshOnExit: true);
    private SessionStatus Get(string id, bool refreshOnExit)
    {
        var record = JsonFiles.ReadObject(FilePath(id));
        var state = SessionStatus.FromStored(record) with { Startup = StartupDiagnostics.Read(paths, id) };
        var incompleteIdentity = (state.Status == "running" && (state.ProcessId is null || state.ProcessStartUtc is null)) || (state.Status == "preparing" && (state.MonitorId is null || state.MonitorStartUtc is null));
        if (incompleteIdentity)
        {
            state.AdditionalData.Remove("activityUnknown");
            state = state with { ActivityUnknown = true };
        }

        if (!incompleteIdentity && state.Status == "running" && !IsRecordedProcessAlive(record) && !IsMonitorAlive(record))
        {
            // The monitor writes exited/failed before exiting; do not override that newer snapshot.
            if (refreshOnExit)
                return Get(id, refreshOnExit: false);
            state.AdditionalData.Remove("readiness");
            state = state with { Status = "interrupted", Readiness = "process_gone" };
        }

        if (!incompleteIdentity && state.Status == "preparing" && !IsMonitorAlive(record))
        {
            if (refreshOnExit)
                return Get(id, refreshOnExit: false);
            state = state with { Status = "interrupted" };
        }

        if (state.Status == "queued" && DateTimeOffset.TryParse(state.CreatedAt, out var created) && DateTimeOffset.UtcNow - created > TimeSpan.FromSeconds(30))
        {
            if (refreshOnExit)
                return Get(id, refreshOnExit: false);
            state = state with { Status = "interrupted" };
        }

        if (!incompleteIdentity && state.Status == "running" && OperatingSystem.IsWindows())
        {
            state = ObserveWindow(state);
        }

        return state;
    }

    private static SessionStatus ObserveWindow(SessionStatus state)
    {
        state = state with { Readiness = "process_started", WindowObservationWarning = null };
        try
        {
            using var process = Process.GetProcessById(state.ProcessId!.Value);
            if (process.HasExited || process.StartTime.ToUniversalTime().ToString("O") != state.ProcessStartUtc)
                return state;
            var visible = GameWindow.IsVisible(process, out var warning);
            return state with { Readiness = visible ? "window_visible" : "process_started", WindowObservationWarning = warning };
        }
        catch (Exception error) when (error is ArgumentException or InvalidOperationException)
        {
            return state;
        }
        catch (System.ComponentModel.Win32Exception error)
        {
            return state with { Readiness = "process_started", WindowObservationWarning = error.Message };
        }
    }

    private static bool IsUnreadable(Exception ex) => ex is IOException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException or UnauthorizedAccessException or ArgumentException or FormatException;
    /// <summary>Lists observed sessions, independently reporting broken records and their activity sidecars.</summary>
    public IReadOnlyList<SessionStatus> List()
    {
        var result = new List<SessionStatus>();
        if (Directory.Exists(paths.SessionsDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(paths.SessionsDirectory, "*.json"))
            {
                try
                {
                    result.Add(Get(Path.GetFileNameWithoutExtension(file)));
                }
                catch (Exception ex) when (IsUnreadable(ex))
                {
                    var broken = new SessionStatus
                    {
                        Id = Path.GetFileNameWithoutExtension(file),
                        Status = "unreadable",
                        Path = file,
                        Error = ex.Message,
                        ActivityUnknown = true
                    };
                    try
                    {
                        if (File.Exists(file + ".activity"))
                            broken = broken with { Activity = JsonFiles.ReadObject(file + ".activity") };
                    }
                    catch (Exception activityError) when (IsUnreadable(activityError))
                    {
                        broken = broken with { ActivityWarning = activityError.Message };
                    }

                    result.Add(broken);
                }
            }
        }

        return result;
    }

    private static string RegistryDirectory(string game) => Path.Combine(Path.GetTempPath(), "Rodel.MinecraftLauncher.Sessions", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ResourceLease.CanonicalPath(game).TrimEnd('\\', '/').ToUpperInvariant()))));
    /// <summary>Checks fresh local session observations and the cross-host registry for a game directory.</summary>
    public bool HasActiveGame(string game) => HasActiveGame(game, List());

    /// <summary>Checks a game directory using a caller's current observation batch, while still reading its cross-host registry.</summary>
    /// <param name="game">The game directory whose running or preparing activity is being observed.</param>
    /// <param name="observedSessions">Sessions freshly read for this observation cycle; callers must not cache these across cycles or mutations.</param>
    /// <returns>Whether local observations or the current cross-host registry indicate active or uncertain activity.</returns>
    public static bool HasActiveGame(string game, IReadOnlyList<SessionStatus> observedSessions)
    {
        var canonical = ResourceLease.CanonicalPath(game).TrimEnd('\\', '/');
        foreach (var listed in observedSessions)
        {
            if (listed.Activity is null && !listed.AdditionalData.ContainsKey("gameDirectory") && !listed.AdditionalData.ContainsKey("status"))
            {
                // Ordinary observations already have typed fields. Do not serialize launch arguments,
                // diagnostics and historical output just to decide whether this game is active.
                if (listed.Status is "queued" or "preparing" or "running" && listed.GameDirectory is { } gameDirectory
                    && string.Equals(ResourceLease.CanonicalPath(gameDirectory).TrimEnd('\\', '/'), canonical, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }

                continue;
            }

            var state = listed.Activity ?? JsonSerializer.SerializeToNode(listed, LaunchJsonContext.Default.SessionStatus)!.AsObject();
            if (state["gameDirectory"]?.GetValue<string>() is { } directory && string.Equals(ResourceLease.CanonicalPath(directory).TrimEnd('\\', '/'), canonical, StringComparison.OrdinalIgnoreCase) && state["status"]?.GetValue<string>() is "queued" or "preparing" or "running")
            {
                if (listed.Status != "unreadable" || IsRecordedProcessAlive(state) || IsMonitorAlive(state) || (state["status"]?.GetValue<string>() == "queued" && DateTimeOffset.TryParse(state["createdAt"]?.GetValue<string>(), out var created) && DateTimeOffset.UtcNow - created < TimeSpan.FromSeconds(30)))
                    return true;
            }
        }

        var shared = RegistryDirectory(game);
        if (Directory.Exists(shared))
        {
            foreach (var file in Directory.EnumerateFiles(shared, "*.json"))
            {
                try
                {
                    if (IsRecordedProcessAlive(JsonFiles.ReadObject(file)))
                        return true;
                }
                catch (FileNotFoundException)
                {
                } // A monitor can remove its record while we enumerate.
                catch (Exception ex) when (IsUnreadable(ex))
                {
                    return true;
                } // This registry belongs only to the requested game directory.
            }
        }

        return false;
    }

    /// <summary>Persists a launch plan as a session record for a monitor to prepare and start; recording does not prove game readiness.</summary>
    public Task<string> CreateAsync(LaunchPlan plan, string? instanceId = null, string? jobId = null, CancellationToken ct = default)
        => CreateCoreAsync(plan, instanceId, jobId, null, ct);

    internal async Task<string> CreateCoreAsync(LaunchPlan plan, string? instanceId, string? jobId, string? repairPlanId, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var arguments = new JsonArray();
        foreach (var argument in plan.Arguments)
            arguments.Add((JsonNode?)JsonValue.Create(argument));
        var natives = new JsonArray();
        foreach (var file in plan.RequiredFiles.Where(f => f.Kind == "native"))
            natives.Add((JsonNode)new JsonObject { ["path"] = file.Path, ["excludes"] = new JsonArray([.. (file.ExtractExcludes ?? []).Select(x => (JsonNode?)JsonValue.Create(x))]) });
        var mappings = new JsonArray();
        var assets = plan.RequiredFiles.Where(f => f.Kind == "asset" && f.Sha1 is not null).GroupBy(f => f.Sha1).ToDictionary(g => g.Key!, g => g.First().Path);
        foreach (var file in plan.RequiredFiles.Where(f => f.Kind == "assetMapped"))
        {
            if (file.Sha1 is not null && assets.TryGetValue(file.Sha1, out var source))
                mappings.Add((JsonNode)new JsonObject { ["source"] = source, ["target"] = file.Path });
        }

        var state = new JsonObject
        {
            ["id"] = id,
            ["instanceId"] = instanceId,
            ["accountId"] = plan.AccountId,
            ["gameBridgeEnabled"] = plan.GameBridgeEnabled,
            ["gameBridgeOffline"] = plan.GameBridgeOffline,
            ["gameBridgeMinecraftDirectory"] = plan.GameBridgeMinecraftDirectory,
            ["useCachedExternalSession"] = plan.UseCachedExternalSession,
            ["server"] = plan.Server,
            ["serverConnectionMode"] = plan.ServerConnectionMode,
            ["jobId"] = jobId,
            ["repairPlanId"] = repairPlanId,
            ["status"] = "queued",
            ["versionId"] = plan.VersionId,
            ["gameLanguage"] = plan.GameLanguage,
            ["fullscreen"] = plan.Fullscreen,
            ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
            ["javaPath"] = plan.JavaPath,
            ["arguments"] = arguments,
            ["javaMajorVersion"] = plan.JavaMajorVersion,
            ["mainClassArgumentIndex"] = plan.MainClassArgumentIndex,
            ["environmentOverrides"] = plan.EnvironmentOverrides?.DeepClone(),
            ["stdoutEncoding"] = plan.StdoutEncoding,
            ["stderrEncoding"] = plan.StderrEncoding,
            ["fileEncoding"] = plan.FileEncoding,
            ["loggingCompatibility"] = JsonSerializer.SerializeToNode(plan.LoggingCompatibility, LaunchJsonContext.Default.LoggingCompatibilityPlan),
            ["javaSelection"] = JsonSerializer.SerializeToNode(plan.JavaSelection, JavaJsonContext.Default.JavaSelectionReport),
            ["memorySelection"] = JsonSerializer.SerializeToNode(plan.MemorySelection, LaunchJsonContext.Default.MemorySelectionReport),
            ["warnings"] = new JsonArray([.. plan.Warnings.Select(x => (JsonNode?)JsonValue.Create(x))]),
            ["gameDirectory"] = plan.WorkingDirectory,
            ["nativesDirectory"] = plan.NativesDirectory,
            ["nativeArchives"] = natives,
            ["assetMappings"] = mappings,
            ["logStorage"] = "memory"
        };
        await JsonFiles.WriteAsync(FilePath(id), state, ct).ConfigureAwait(false);
        await WriteActivityAsync(id, state).ConfigureAwait(false);
        return id;
    }

    public async Task RunMonitorAsync(string id, CancellationToken preparationCancellation = default)
    {
        var state = GetRecord(id);
        var startup = new StartupDiagnostics(paths, id, state["stdoutEncoding"]?.ToString(), state["stderrEncoding"]?.ToString());
        await using var startupDisposal = startup.ConfigureAwait(false);
        try
        {
            var capture = await GameLogHost.StartAsync(paths).ConfigureAwait(false);
            await using var captureDisposal = capture.ConfigureAwait(false);
            state["logPipe"] = capture.PipeName;
            if (capture.Warning is { } captureWarning) state["logWarning"] = captureWarning;
            state["monitorId"] = Environment.ProcessId;
            state["monitorStartUtc"] = Process.GetCurrentProcess().StartTime.ToUniversalTime().ToString("O");
            state["status"] = "preparing";
            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            await WriteActivityAsync(id, state).ConfigureAwait(false);
            var gameDirectory = state["gameDirectory"]!.GetValue<string>();
            using var preparationLease = await ResourceLease.AcquireAsync(resource: gameDirectory, cancellationToken: preparationCancellation).ConfigureAwait(false);
            preparationCancellation.ThrowIfCancellationRequested();
            startup.Mark("logging_preparation");
            await PrepareLoggingAsync(state).ConfigureAwait(false);
            Directory.CreateDirectory(gameDirectory);
            startup.Mark("native_preparation");
            var nativeBase = state["nativesDirectory"]!.GetValue<string>();
            // Each archive set gets its own directory; replacing native DLLs in a running game is unsafe.
            using var digest = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (var archive in state["nativeArchives"]!.AsArray())
            {
                preparationCancellation.ThrowIfCancellationRequested();
                var input = File.OpenRead(archive is JsonValue ? archive.GetValue<string>() : archive!["path"]!.GetValue<string>());
                await using var inputDisposal = input.ConfigureAwait(false);
                digest.AppendData(await SHA256.HashDataAsync(input, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false));
                // The same archive with a different extraction policy needs a separate directory.
                if (archive is JsonObject)
                    digest.AppendData(Encoding.UTF8.GetBytes(archive["excludes"]?.ToJsonString() ?? "[]"));
            }

            var nativesDirectory = Path.Combine(nativeBase, Convert.ToHexStringLower(digest.GetHashAndReset())[..16]);
            foreach (var mapping in state["assetMappings"] as JsonArray ?? [])
            {
                preparationCancellation.ThrowIfCancellationRequested();
                var target = mapping!["target"]!.GetValue<string>();
                var source = mapping["source"]!.GetValue<string>();
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (!File.Exists(target))
                    File.Copy(source, target);
            }

            using (await ResourceLease.AcquireAsync(resource: nativesDirectory, cancellationToken: preparationCancellation).ConfigureAwait(false))
            {
                Directory.CreateDirectory(nativesDirectory);
                foreach (var archive in state["nativeArchives"]!.AsArray())
                {
                    preparationCancellation.ThrowIfCancellationRequested();
                    ExtractNativeArchive(archive is JsonValue ? archive.GetValue<string>() : archive!["path"]!.GetValue<string>(), nativesDirectory, archive is JsonObject ? (archive["excludes"] as JsonArray)?.Select(x => x!.GetValue<string>()).ToArray() : null);
                }
            }

            var start = new ProcessStartInfo(state["javaPath"]!.GetValue<string>())
            {
                WorkingDirectory = gameDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var originalArguments = state["arguments"]!.AsArray().Select(argument => argument!.GetValue<string>().Replace(nativeBase, nativesDirectory, StringComparison.Ordinal)).ToArray();
            string? token = null;
            var mainClassIndex = state["mainClassArgumentIndex"]?.GetValue<int>();
            if (state["accountId"]?.GetValue<string>() is { } accountId)
            {
                startup.Mark("authentication");
                var identities = new Application.IdentityService(paths);
                var credentials = await identities.GetLaunchCredentialsAsync(accountId, preparationCancellation, state["useCachedExternalSession"]?.GetValue<bool>() == true).ConfigureAwait(false);
                token = credentials.MinecraftToken;
                originalArguments = [.. originalArguments.Select(a => a.Replace("__RODEL_MINECRAFT_TOKEN__", token, StringComparison.Ordinal)
                    .Replace("__RODEL_CLIENT_ID__", credentials.ClientId, StringComparison.Ordinal)
                    .Replace("__RODEL_XUID__", credentials.Xuid, StringComparison.Ordinal))];
                var identity = identities.Get(accountId);
                if (identity.Type == "external")
                {
                    var agent = await identities.PrepareExternalLoginComponentAsync(ct: preparationCancellation).ConfigureAwait(false);
                    string[] injected = ["-javaagent:" + agent.Path + "=" + identity.AuthServer,
                        "-Dauthlibinjector.yggdrasil.prefetched=" + Convert.ToBase64String(Encoding.UTF8.GetBytes(
                            (credentials.AuthenticationMetadata ?? throw new InvalidDataException("The protected external session has no authentication metadata.")).ToJsonString()))];
                    originalArguments = [.. injected, .. originalArguments.Select(a => a.Replace("__RODEL_EXTERNAL_PLAYER__", identity.Name!, StringComparison.Ordinal))];
                    if (mainClassIndex is not null) mainClassIndex += injected.Length;
                }
            }

            GameBridgeOwner? bridge = null;
            string? bridgeBindings = null;
            string? bridgeInputHooks = null;
            if (state["gameBridgeEnabled"]?.GetValue<bool>() == true)
            {
                startup.Mark("bridge_preparation");
                try
                {
                    if (state["javaMajorVersion"]?.GetValue<int>() is not { } javaMajor || javaMajor < 6) throw new InvalidDataException("The observation bridge requires Java 6 or newer.");
                    using var bridgeDeadline = CancellationTokenSource.CreateLinkedTokenSource(preparationCancellation);
                    bridgeDeadline.CancelAfter(TimeSpan.FromSeconds(20));
                    var (Jar, Bindings, InputHooks) = await BridgeComponent.PrepareAsync(paths, state["gameBridgeMinecraftDirectory"]!.GetValue<string>(), state["versionId"]!.GetValue<string>(), state["gameBridgeOffline"]?.GetValue<bool>() == true,
                        bridgeDeadline.Token, javaMajor, state["gameDirectory"]!.GetValue<string>()).ConfigureAwait(false);
                    bridge = new GameBridgeOwner(paths, id);
                    bridgeBindings = Bindings;
                    bridgeInputHooks = InputHooks;
                    originalArguments = ["-javaagent:" + Jar, .. originalArguments];
                    if (mainClassIndex is not null) mainClassIndex++;
                }
                catch (Exception error) when (error is IOException or InvalidDataException or HttpRequestException or JsonException or OperationCanceledException or System.Net.Sockets.SocketException or UnauthorizedAccessException)
                {
                    preparationCancellation.ThrowIfCancellationRequested();
                    state["gameBridgeError"] = "GAME_BRIDGE_PREPARATION_FAILED";
                    state["gameBridgeDiagnostic"] = error is InvalidDataException ? error.Message : error.GetType().Name;
                    state["warnings"]!.AsArray().Add((JsonNode?)JsonValue.Create("The optional game bridge is unavailable; normal game startup continues."));
                }
            }

            await using var bridgeLifetime = ((IAsyncDisposable?)bridge ?? Stream.Null).ConfigureAwait(false);
            startup.Mark("argument_preparation");
            var sensitiveIndex = token is null ? -1 : Array.FindIndex(originalArguments, a => a.Contains(token, StringComparison.Ordinal));
            var (Arguments, Strategy) = await JavaCommandLine.PrepareAsync(start.FileName, originalArguments, state["javaMajorVersion"]?.GetValue<int>(), gameDirectory, Path.Combine(paths.SessionsDirectory, id + ".launch"), mainClassArgumentIndex: mainClassIndex, unpersistedArgumentIndex: sensitiveIndex < 0 ? null : sensitiveIndex, ct: System.Threading.CancellationToken.None).ConfigureAwait(false);
            state["commandLineStrategy"] = Strategy;
            foreach (var argument in Arguments)
                start.ArgumentList.Add(argument);
            foreach (var (key, value) in state["environmentOverrides"] as JsonObject ?? [])
            {
                if (value is null)
                    start.Environment.Remove(key);
                else
                    start.Environment[key] = value.ToString();
            }

            // Reserved bootstrap values are assigned after persisted caller overrides and never saved.
            foreach (var key in new[] { "RODEL_BRIDGE_PORT", "RODEL_BRIDGE_SECRET", "RODEL_BRIDGE_SESSION", "RODEL_BRIDGE_INCARNATION", "RODEL_BRIDGE_BINDINGS", "RODEL_BRIDGE_INPUT_HOOKS" }) start.Environment.Remove(key);
            bridge?.Configure(start, bridgeBindings!, bridgeInputHooks);

            preparationCancellation.ThrowIfCancellationRequested();
            if (state["gameLanguage"]?.GetValue<string>() is { } language)
            {
                startup.Mark("language_sync");
                try
                {
                    await GameSettingsService.ApplyLaunchLanguageAsync(gameDirectory, language, preparationCancellation).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or EncoderFallbackException)
                {
                    state["gameLanguageWarning"] = error.Message;
                    state["warnings"]!.AsArray().Add((JsonNode?)JsonValue.Create("Game language could not be synchronized; existing options were retained: " + error.Message));
                }
            }

            if (state["fullscreen"]?.GetValue<bool>() is { } fullscreen)
            {
                startup.Mark("window_mode_sync");
                try
                {
                    await GameSettingsService.ApplyLaunchWindowModeAsync(gameDirectory, fullscreen, preparationCancellation).ConfigureAwait(false);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or DecoderFallbackException or EncoderFallbackException)
                {
                    state["windowModeWarning"] = error.Message;
                    state["warnings"]!.AsArray().Add((JsonNode?)JsonValue.Create("Game window mode could not be synchronized; existing options were retained: " + error.Message));
                }
            }

            using var process = Process.Start(start) ?? throw new IOException("Java did not start.");
            var output = capture.Stdout.CaptureAsync(token is null ? process.StandardOutput.BaseStream : new SecretRedactingStream(process.StandardOutput.BaseStream, token));
            var errors = capture.Stderr.CaptureAsync(token is null ? process.StandardError.BaseStream : new SecretRedactingStream(process.StandardError.BaseStream, token));
            startup.Bind(process, capture);
            bridge?.Bind(process);
            state["processId"] = process.Id;
            state["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O");
            state["status"] = "running";
            state["readiness"] = "process_started";
            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            await JsonFiles.WriteAsync(Path.Combine(RegistryDirectory(gameDirectory), id + ".json"), new JsonObject { ["id"] = id, ["gameDirectory"] = gameDirectory, ["processId"] = process.Id, ["processStartUtc"] = state["processStartUtc"]!.DeepClone() }, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            preparationLease.Dispose();
            await WriteActivityAsync(id, state).ConfigureAwait(false);
            await process.WaitForExitAsync(cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            await Task.WhenAll(output, errors).ConfigureAwait(false);
            startup.Complete();
            var stopped = File.Exists(StopMarker(id));
            state["stopRequested"] = stopped;
            foreach (var stderr in new[] { false, true })
            {
                var key = stderr ? "stderrLog" : "stdoutLog";
                var snapshot = (stderr ? capture.Stderr : capture.Stdout).Read(0, GameLogBuffer.Capacity);
                state[key + "Status"] = JsonSerializer.SerializeToNode(snapshot.Capture, LaunchJsonContext.Default.LogCaptureStatus);
                if (process.ExitCode != 0 && !stopped)
                {
                    var file = Path.Combine(paths.LogsDirectory, id + (stderr ? ".stderr.log" : ".stdout.log"));
                    try
                    {
                        await JsonFiles.WriteBytesAsync(file, snapshot.Bytes, CancellationToken.None).ConfigureAwait(false);
                        state[key] = file;
                        state["logStorage"] = "crash-snapshot";
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        state[key + "Warning"] = "Crash console snapshot could not be saved: " + error.Message;
                    }
                }
            }

            state["exitCode"] = process.ExitCode;
            state["status"] = "exited";
            state["exitedAt"] = DateTimeOffset.UtcNow.ToString("O");
            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
            await WriteActivityAsync(id, state).ConfigureAwait(false);
            File.Delete(Path.Combine(RegistryDirectory(gameDirectory), id + ".json"));
        }
        catch (Exception exception)
        {
            startup.End(exception is OperationCanceledException ? "preparation_canceled" : "monitor_failed");
            state["status"] = exception is OperationCanceledException ? "canceled" : "failed";
            state["error"] = exception.Message;
            if (exception is LauncherOperationException problem)
            {
                state["errorCode"] = problem.Code;
                state["errorDetails"] = problem.Details?.DeepClone();
            }

            await JsonFiles.WriteAsync(FilePath(id), state, cancellationToken: System.Threading.CancellationToken.None).ConfigureAwait(false);
        }
        finally
        {
            ClearStopMarker(id);
        }
    }

    /// <summary>Observes a persisted session until it reaches a terminal or interrupted state.</summary>
    public async Task<SessionStatus> WaitForExitAsync(string id, CancellationToken ct = default)
    {
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = Get(id);
            if (state.Status is "exited" or "failed" or "interrupted" or "canceled")
                return state;
            await Task.Delay(200, ct).ConfigureAwait(false);
        }
    }

    private Task WriteActivityAsync(string id, JsonObject state)
    {
        var activity = new JsonObject();
        foreach (var key in new[]
        {
            "gameDirectory",
            "status",
            "createdAt",
            "processId",
            "processStartUtc",
            "monitorId",
            "monitorStartUtc"
        }

        )
        {
            activity[key] = state[key]?.DeepClone();
        }

        return JsonFiles.WriteAsync(FilePath(id) + ".activity", activity);
    }

    private static bool IsRecordedProcessAlive(JsonObject state)
    {
        if (state["processId"]?.GetValue<int>() is not { } pid)
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().ToString("O") == state["processStartUtc"]?.GetValue<string>();
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    private static bool IsMonitorAlive(JsonObject state)
    {
        if (state["monitorId"]?.GetValue<int>() is not { } pid)
            return false;
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited && process.StartTime.ToUniversalTime().ToString("O") == state["monitorStartUtc"]?.GetValue<string>();
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    /// <summary>Observes preparation until process start, terminal state or the bounded observation timeout.</summary>
    public async Task<SessionStatus> WaitForStartAsync(string id, CancellationToken ct = default)
    {
        var watch = Stopwatch.StartNew();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var state = Get(id);
            if (state.Status is "running" or "exited" or "failed" or "interrupted" or "canceled")
                return state;
            if (watch.Elapsed > TimeSpan.FromSeconds(30))
                return state;
            await Task.Delay(100, ct).ConfigureAwait(false);
        }
    }

    /// <summary>Signals only the recorded process or verified window and reports the request without inventing exit completion.</summary>
    public SessionStopResult Stop(string id, bool force = false)
    {
        var state = Get(id);
        if (state.Status is "exited" or "failed" or "interrupted" or "canceled")
        {
            return new InactiveSessionStopResult(id, state.Status, "The game process is no longer running.");
        }

        var pid = state.ProcessId ?? throw new InvalidOperationException("Session has no game process.");
        using var process = Process.GetProcessById(pid);
        if (process.StartTime.ToUniversalTime().ToString("O") != state.ProcessStartUtc)
            throw new InvalidOperationException("The recorded game process has exited; its PID has been reused.");
        var requested = force;
        int? windowProcessId = null;
        string? warning = null;
        // Publish intent before signalling the child so a fast user-requested exit is not archived as a crash.
        try { using (File.Open(StopMarker(id), FileMode.OpenOrCreate, FileAccess.Write, FileShare.ReadWrite)) { } }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warning = "Stop intent could not be recorded; a nonzero exit may be saved as an abnormal exit: " + error.Message;
            Trace.TraceWarning("{0}", warning);
        }

        try
        {
            if (force)
                process.Kill(entireProcessTree: true);
            else
                requested = GameWindow.RequestClose(process, out windowProcessId);
        }
        catch
        {
            ClearStopMarker(id);
            throw;
        }

        if (!requested) ClearStopMarker(id);
        return new RequestedSessionStopResult(id, requested, force, warning,
            force ? "terminate-recorded-process-tree" : "system-menu-close", windowProcessId,
            windowProcessId is null ? null : windowProcessId == pid ? "recorded-process" : "descendant",
            requested ? "Stop requested; query session for exit." : "No game window accepted a close request. Use --force to terminate this session.");
    }

    private string StopMarker(string id) => FilePath(id) + ".stop-requested";

    private void ClearStopMarker(string id)
    {
        try { File.Delete(StopMarker(id)); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            Trace.TraceWarning("Session stop marker cleanup failed: {0}", error.Message);
        }
    }

    private static void ExtractNativeArchive(string archivePath, string destination, string[]? excludes = null)
    {
        var root = Path.GetFullPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var physicalRoot = ResourceLease.CanonicalPath(destination).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var name = entry.FullName.Replace('\\', '/');
            if (name.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(entry.Name) || (excludes ?? []).Any(prefix => name.StartsWith(prefix.Replace('\\', '/'), StringComparison.Ordinal)))
                continue;
            var target = Path.GetFullPath(Path.Combine(root, entry.FullName));
            if (!target.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Native archive path escapes destination.");
            if (!ResourceLease.CanonicalPath(target).StartsWith(physicalRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("A linked native archive target escapes its extraction directory.");
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            if (File.Exists(target) && new FileInfo(target).Length == entry.Length)
            {
                using var expected = entry.Open();
                using var existing = File.OpenRead(target);
                if (SHA256.HashData(expected).AsSpan().SequenceEqual(SHA256.HashData(existing)))
                    continue;
            }

            var temporary = target + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                entry.ExtractToFile(temporary);
                File.Move(temporary, target, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }
        }
    }
}