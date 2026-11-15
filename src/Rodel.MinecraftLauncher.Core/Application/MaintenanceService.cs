// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class MaintenanceService(LauncherPaths paths)
{
    /// <summary>Synchronously reads advisory session diagnostics for synchronous hosts.</summary>
    public SessionDiagnosis DiagnoseSession(string sessionId) => DiagnoseSessionAsync(sessionId).GetAwaiter().GetResult();
    /// <summary>Combines bounded game logs and time-associated crash reports, retaining independent read failures.</summary>
    public async Task<SessionDiagnosis> DiagnoseSessionAsync(string sessionId, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        var state = new SessionService(paths).Get(sessionId);
        if (state.Status is "failed" or "interrupted" && state.ProcessId is null && state.Error is { Length: > 0 })
        {
            var active = state.GameDirectory is { } directory && new SessionService(paths).HasActiveGame(directory);
            return new SessionDiagnosis(sessionId, state.Status, state.ExitCode, [], [], [], [], new Dictionary<string, LogStreamSummary>())
            {
                PreparationFailure = state,
                GameActive = active,
                Problem = LaunchProblem.DescribePreparation(active, null, state)
            };
        }

        var findings = new List<DiagnosticFinding>();
        var rules = new[]
        {
            ("OutOfMemoryError", "JAVA_OUT_OF_MEMORY", "Review the memory limit and the active mod set."),
            ("Could not reserve enough space", "JAVA_MEMORY_RESERVATION", "Reduce the requested memory or select a 64-bit Java runtime."),
            ("UnsupportedClassVersionError", "JAVA_VERSION_MISMATCH", "Select the Java version recommended by the game or mod."),
            ("incompatible mod set", "MOD_DEPENDENCY_CONFLICT", "Read the loader's dependency list and adjust the named mods."),
            ("MixinApplyError", "MIXIN_FAILURE", "Inspect the named mixin/mod and its supported game version."),
            ("ClassNotFoundException", "CLASS_NOT_FOUND", "Inspect the named class; repair libraries or restore the responsible local mod."),
            ("AccessDeniedException", "FILE_ACCESS_DENIED", "Check the named path, running game processes and Windows file permissions."),
            ("GLFW error 65542", "OPENGL_UNAVAILABLE", "Inspect the graphics driver and the Java architecture.")
        };
        var found = new HashSet<string>();
        var excerpt = await new LogService(paths).ReadAsync(sessionId, tail: 1000, byteBudget: 131072, ct: ct).ConfigureAwait(false);
        var parsedLogs = CrashReportService.Analyze(excerpt.StdoutLog.Concat(excerpt.StderrLog));
        foreach (var key in new[]
        {
            "stdoutLog",
            "stderrLog"
        }

        )
        {
            foreach (var line in key == "stdoutLog" ? excerpt.StdoutLog : excerpt.StderrLog)
            {
                ct.ThrowIfCancellationRequested();
                foreach (var (pattern, code, suggestion) in rules)
                {
                    if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase) && found.Add(code))
                        findings.Add(new(code, line.Length <= 1024 ? line : line[..1024], line.Length > 1024, suggestion, Source: key));
                }
            }
        }

        var reports = new List<CrashAnalysis>();
        var reportWarnings = new List<DiagnosticReadWarning>();
        if (state.GameDirectory is { } game && DateTimeOffset.TryParse(state.CreatedAt, out var start))
        {
            var end = DateTimeOffset.TryParse(state.ExitedAt, out var exited) ? exited.UtcDateTime.AddSeconds(2) : DateTime.UtcNow;
            foreach (var directory in new[]
            {
                game,
                Path.Combine(game, "crash-reports")
            }

            )
            {
                if (!Directory.Exists(directory))
                    continue;
                string[] available;
                try
                {
                    available = Directory.GetFiles(directory);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    reportWarnings.Add(new(directory, ex.Message));
                    continue;
                }

                foreach (var file in available.Where(x => Path.GetFileName(x).StartsWith("crash-", StringComparison.OrdinalIgnoreCase) || Path.GetFileName(x).StartsWith("hs_err_pid", StringComparison.OrdinalIgnoreCase)).Where(x => File.GetLastWriteTimeUtc(x) >= start.UtcDateTime.AddSeconds(-2) && File.GetLastWriteTimeUtc(x) <= end).OrderByDescending(File.GetLastWriteTimeUtc).Take(5))
                {
                    try
                    {
                        reports.Add(await CrashReportService.AnalyzeAsync(file: file, ct: ct, encoding: state.FileEncoding).ConfigureAwait(false));
                    }
                    catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                    {
                        reportWarnings.Add(new(file, ex.Message));
                    }
                }
            }
        }

        foreach (var item in parsedLogs.Findings)
        {
            if (found.Add(item.Code))
                findings.Add(item);
        }

        for (var index = 0; index < findings.Count; index++)
        {
            if (findings[index].Code == "REGISTRY_MISMATCH")
                findings[index] = findings[index] with { Scope = "session-history-unattributed" };
        }

        var eventRead = await new LogEventService(paths).ReadAsync(sessionId, budget: 262144, ct: ct).ConfigureAwait(false);
        var eventSnapshot = SnapshotLogEvents(eventRead);
        var connection = state.Server is { } server
            ? Servers.ServerConnectionService.Analyze(server, [.. eventSnapshot.Select(item => item.Event)]) : null;
        if (connection is not null)
        {
            connection.SessionId = sessionId;
            connection.ProcessStatus = state.Status;
            connection.GameActive = state.GameDirectory is { } directory && new SessionService(paths).HasActiveGame(directory);
            connection.LogRead = eventRead.Streams;
            connection.Warnings = eventRead.Warnings;
            connection.Bounded = true;
        }

        var reportedErrors = ReadReportedErrors(eventSnapshot, eventRead, connection);
        if (!reportedErrors.OfType<ParsedLaunchError>().Any(error => error.Stream == "stderrLog")
            && state.ExitCode is { } exitCode && exitCode != 0
            && excerpt.StderrLog.Count > 0)
        {
            reportedErrors.Add(new RawProcessLaunchError(string.Join("\n", excerpt.StderrLog)));
        }

        var result = new SessionDiagnosis(sessionId, state.Status, state.ExitCode, findings, parsedLogs.DependencyEvidence, reports, reportWarnings, excerpt.Streams)
        {
            ReportedErrors = reportedErrors,
            CurrentReportedErrors = [.. reportedErrors.Where(error => error.AttributionScope != "unattributed-session-log")],
            ErrorLogRead = eventRead.Streams,
            ErrorLogWarnings = eventRead.Warnings,
            Details = parsedLogs.Details,
            SelectedJavaMajor = state.JavaMajorVersion,
            GameActive = state.GameDirectory is { } gameDirectory && new SessionService(paths).HasActiveGame(gameDirectory),
            LogExcerpt = new LaunchLogExcerpt(excerpt.StdoutLog, excerpt.StderrLog),
            ServerConnection = connection
        };
        return result with { Problem = LaunchProblem.Describe(result) };
    }

    /// <summary>Reports usable Java and required-file issues without changing an instance or blocking manual configuration.</summary>
    public async Task<InstanceDiagnosis> DiagnoseAsync(string instanceId, bool verifyHashes = false, CancellationToken ct = default)
    {
        var instance = new InstanceStore(paths).Get(instanceId);
        var effective = new SettingsService(paths).GetEffective(instance);
        var values = effective.Values;
        var version = values["versionId"]?.GetValue<string>() ?? instance.VersionId;
        var issues = new JsonArray();
        var result = new InstanceDiagnosis
        {
            InstanceId = instanceId,
            GameDirectory = instance.GameDirectory,
            Issues = issues
        };
        if (version is null)
        {
            issues.Add((JsonNode)new JsonObject { ["code"] = "VERSION_NOT_SELECTED", ["message"] = "Select a version with config set versionId --instance ID." });
            return result;
        }

        if (effective.Warnings.OfType<JsonObject>().FirstOrDefault(value => value["code"]?.ToString() == "JAVA_RUNTIME_UNAVAILABLE") is { } javaIssue)
        {
            issues.Add(javaIssue.DeepClone());
            result.UsableJava = false;
            return result;
        }

        LaunchPlan plan;
        try
        {
            plan = await new LaunchPlanner(paths).BuildAsync(minecraftDirectory: instance.MinecraftDirectory, gameDirectory: instance.GameDirectory, versionId: version, javaPath: values["javaPath"]?.GetValue<string>() ?? instance.JavaPath, ct: ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or ArgumentException)
        {
            issues.Add((JsonNode)new JsonObject { ["code"] = "METADATA_UNAVAILABLE", ["message"] = ex.Message, ["suggestion"] = "Install/repair official files, or provide local custom version metadata." });
            return result;
        }

        var runtime = await new JavaDiscovery(paths).ProbeAsync(plan.JavaPath, ct).ConfigureAwait(false);
        result.Java = new(runtime.Path, runtime.IsUsable, runtime.MajorVersion);
        result.Warnings = plan.Warnings;
        foreach (var file in plan.RequiredFiles)
        {
            ct.ThrowIfCancellationRequested();
            var valid = file.Exists && (!verifyHashes || await DownloadService.ValidAsync(file.Path, file.Sha1, null, ct).ConfigureAwait(false));
            if (!valid)
                issues.Add((JsonNode)new JsonObject { ["code"] = file.Exists ? "HASH_MISMATCH" : "FILE_MISSING", ["path"] = file.Path, ["kind"] = file.Kind, ["repairable"] = file.DownloadUrl is not null || file.Kind == "assetMapped" });
        }

        result.CheckedFiles = plan.RequiredFiles.Length;
        result.HashesVerified = verifyHashes;
        result.UsableJava = runtime.IsUsable;
        return result;
    }

    /// <summary>Publishes available console bytes and their provenance without claiming a complete historical log.</summary>
    public async Task<LogExportResult> ExportLogsAsync(string sessionId, string destination, CancellationToken ct = default)
    {
        var session = JsonSerializer.SerializeToNode(new SessionService(paths).Get(sessionId), LaunchJsonContext.Default.SessionStatus)!.AsObject();
        var snapshots = await new LogService(paths).SnapshotAsync(sessionId, ct).ConfigureAwait(false);
        var warnings = new List<string>();
        var capture = new JsonObject();
        foreach (var (name, snapshot) in snapshots)
        {
            capture[name] = new JsonObject
            {
                ["source"] = snapshot.Source,
                ["firstOffset"] = snapshot.FirstOffset,
                ["offset"] = snapshot.Offset,
                ["nextOffset"] = snapshot.Offset + snapshot.Bytes.Length,
                ["totalBytes"] = snapshot.TotalBytes,
                ["warning"] = snapshot.Warning
            };
            if (snapshot.Warning is not null) warnings.Add(snapshot.Warning);
        }

        session["logWindows"] = capture;
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            using (var archive = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                session.Remove("environmentOverrides");
                session.Remove("arguments"); // The launch plan can contain provider-supplied credentials in later extensions.
                {
                    var writer = new StreamWriter(archive.CreateEntry("session.json").Open());
                    await using (writer.ConfigureAwait(false))
                        await writer.WriteAsync(session.ToJsonString().AsMemory(), ct).ConfigureAwait(false);
                }

                foreach (var name in new[]
                {
                    "stdoutLog",
                    "stderrLog"
                }

                )
                {
                    var snapshot = snapshots[name];
                    if (snapshot.Source == "unavailable") continue;
                    var output = archive.CreateEntry(name + ".txt", CompressionLevel.Fastest).Open();
                    await using var outputDisposal = output.ConfigureAwait(false);
                    await output.WriteAsync(snapshot.Bytes, ct).ConfigureAwait(false);
                }
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
            return new(destination, sessionId, new FileInfo(destination).Length, warnings);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    /// <summary>Inventories cache files and reports unreadable entries independently.</summary>
    public CacheInventory CacheStatus()
    {
        var files = new List<CacheFileSummary>();
        var warnings = new List<CacheReadWarning>();
        long bytes = 0;
        foreach (var path in CacheFiles())
        {
            try
            {
                var info = new FileInfo(path);
                bytes += info.Length;
                files.Add(new(path, info.Length, info.LastWriteTimeUtc.ToString("O")));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                warnings.Add(new(path, ex.Message));
            }
        }

        return new(paths.CacheDirectory, bytes, files, warnings);
    }

    /// <summary>Assesses or reclaims cache files through the shared ownership and reference policy.</summary>
    public Task<CachePruneResult> PruneCacheAsync(int olderThanDays = 30, bool plan = true, CancellationToken ct = default) => new CacheService(paths).PruneAsync(olderThanDays, plan, ct);
    /// <summary>Queries optional release metadata without making release availability a launch prerequisite.</summary>
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Performance", "CA1822:Mark members as static", Justification = "Retain the public service instance API for callers and future instance configuration.")]
    public async Task<UpdateCheck> CheckUpdateAsync(CancellationToken ct = default)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(20)
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Rodel.MinecraftLauncher/0.1.0");
        var token = Environment.GetEnvironmentVariable("RODEL_GITHUB_TOKEN");
        if (!string.IsNullOrWhiteSpace(token))
            client.DefaultRequestHeaders.Authorization = new("Bearer", token);
        using var response = await client.GetAsync("https://api.github.com/repos/Richasy/Rodel.MinecraftLauncher/releases/latest", ct).ConfigureAwait(false);
        if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new UnavailableUpdate("No accessible release was returned. Private releases require RODEL_GITHUB_TOKEN.");
        }

        response.EnsureSuccessStatusCode();
        var release = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false))!;
        return new AvailableUpdate(release["tag_name"]?.DeepClone(), release["html_url"]?.DeepClone(), release["published_at"]?.DeepClone());
    }

    private IEnumerable<string> CacheFiles() => Directory.Exists(paths.CacheDirectory) ? Directory.EnumerateFiles(paths.CacheDirectory, "*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = true }) : [];
}