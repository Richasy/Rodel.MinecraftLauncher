// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

/// <summary>
/// Executes declared CLI commands and owns their background-host and progress-reporting lifecycle.
/// Feature-specific argument mapping and operation handling belong to the command groups.
/// </summary>
/// <param name="paths">The fixed launcher data paths used by this host.</param>
/// <param name="monitorExecutable">The executable used to start detached job and game-session hosts.</param>
/// <param name="monitorPrefixArguments">Arguments preceding the host command, such as a managed CLI assembly path.</param>
/// <param name="reportEvent">An optional consumer of structured progress events.</param>
/// <param name="allowInteractiveSecrets">Whether this directly invoked CLI may read credentials from its interactive input.</param>
/// <param name="clientRequestId">Optional frozen request identity for idempotent Core job admission.</param>
public sealed class LauncherApplication(LauncherPaths paths, string monitorExecutable, IReadOnlyList<string>? monitorPrefixArguments = null,
    Action<JsonObject>? reportEvent = null, bool allowInteractiveSecrets = false, string? clientRequestId = null)
{
    private readonly AsyncLocal<string?> _activeJob = new();
    private long _lastProgress;

    /// <summary>Gets the lazily created Core client configured with this host's session monitor and progress sink.</summary>
    public LauncherClient Client => LazyInitializer.EnsureInitialized(ref field, () => new LauncherClient(paths, new ProcessSessionHost(monitorExecutable, monitorPrefixArguments), new InlineProgress<LauncherProgress>(ReportClientProgress)));

    /// <summary>Gets the fixed data-root paths; per-command arguments do not replace this host configuration.</summary>
    public LauncherPaths Paths => paths;
    internal Action<JsonObject>? EventSink => reportEvent;
    internal bool AllowsInteractiveSecrets => allowInteractiveSecrets;
    internal AiHostOptions AssistantHost => new() { ExecutablePath = monitorExecutable, PrefixArguments = monitorPrefixArguments ?? [] };
    internal async Task<JobStatus> StartToolTaskAsync(LauncherTaskRequest request, string? requestId, CancellationToken ct)
    {
        var job = await Client.Tasks.QueueAsync(request, requestId, ct).ConfigureAwait(false);
        if (job.ExistingRequest == true) return job;
        // MCP clients may terminate their server's entire process tree on disconnect. Let a
        // short-lived dispatcher exit before returning, so the durable worker is independent.
        using var dispatcher = StartHostProcess(LauncherCommands.Hosting.JobDispatch, job.Id!);
        await dispatcher.WaitForExitAsync(ct).ConfigureAwait(false);
        if (dispatcher.ExitCode != 0) throw new IOException(S.ErrorHostStartFailed);
        return job;
    }

    internal async Task DispatchToolTaskAsync(string id)
    {
        var job = Client.Jobs.Get(id);
        if (job.Status != "queued") return;
        var (Id, StartUtc) = StartHost(LauncherCommands.Hosting.JobHost, job.Id!);
        await Client.Jobs.MarkDispatchedAsync(job.Id!, Id, StartUtc).ConfigureAwait(false);
    }
    internal LauncherApplication WithClientRequestId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _)) throw new ArgumentException("A client request identity must be a 32-digit GUID.");
        return new(paths, monitorExecutable, monitorPrefixArguments, reportEvent, allowInteractiveSecrets, id);
    }

    /// <summary>Gets the instance registry rooted in this host's data directory.</summary>
    public InstanceStore Instances { get; } = new(paths);

    internal async Task<JsonNode?> ExecuteAsync(ParseResult args, CancellationToken ct)
    {
        if (LauncherCommands.IsHelp(args))
            return LauncherCommandHelp.Describe(LauncherCommands.HelpCommand(args));
        if (args.CommandResult.Command is not LauncherCommand command)
            throw new ArgumentException(S.ErrorTerminalHostCommand);
        if (args.GetValue(CliOptions.Background) && LauncherCommands.Path(args).Any(item => item.Command == LauncherCommands.Accounts.Root))
            throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", S.ErrorAccountForegroundRequired);
        using var cacheUse = command.UsesCache ? await CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false) : null;
        var jobs = new JobService(paths);
        if (args.GetValue(CliOptions.Background))
        {
            var job = await CommandJobs.CreateCommandAsync(jobs, paths, args, ct: ct, clientRequestId: clientRequestId).ConfigureAwait(false);
            if (job.ExistingRequest == true) return JsonSerializer.SerializeToNode(job, ApplicationJsonContext.Job);
            var (Id, StartUtc) = StartHost(LauncherCommands.Hosting.JobHost, job.Id!);
            await jobs.MarkDispatchedAsync(job.Id!, Id, StartUtc).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(job, ApplicationJsonContext.Job);
        }

        return await ExecuteWithEventsAsync(args, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Executes an existing persisted job using its typed request or declared CLI command request.
    /// This does not create a new attempt; job state and failures are persisted by the job executor.
    /// </summary>
    /// <param name="id">The identifier of the persisted job to execute.</param>
    /// <param name="ct">Cancels execution without implicitly stopping an independent game session.</param>
    /// <returns>The operation result, or null when the operation has no result payload.</returns>
    public async Task<JsonNode?> RunJobAsync(string id, CancellationToken ct = default)
    {
        _activeJob.Value = id;
        try
        {
            if (Client.Jobs.Get(id).Request?.Operation == "launcher-task")
                return await Client.Tasks.RunAsync(id, ct).ConfigureAwait(false);
            return await Client.Jobs.RunAsync(id, ExecuteJobAsync, ct).ConfigureAwait(false);
        }
        finally
        {
            _activeJob.Value = null;
        }
    }

    internal async Task<JsonNode?> ResumeJobAsync(string id, CancellationToken ct)
    {
        var jobs = Client.Jobs;
        var previous = jobs.Get(id);
        if (previous.Status is "running" or "queued" or "succeeded")
            return JsonSerializer.SerializeToNode(previous, ApplicationJsonContext.Job);
        if (previous.CanResume != true)
            throw new LauncherOperationException("JOB_NOT_RESUMABLE", S.ErrorJobNotResumable, JsonSerializer.SerializeToNode(previous, ApplicationJsonContext.Job));
        var next = await jobs.ResumeAsync(id, ct).ConfigureAwait(false);
        var (Id, StartUtc) = StartHost(LauncherCommands.Hosting.JobHost, next.Id!);
        await jobs.MarkDispatchedAsync(next.Id!, Id, StartUtc).ConfigureAwait(false);
        return JsonSerializer.SerializeToNode(next, ApplicationJsonContext.Job);
    }

    private async Task<JsonNode?> ExecuteWithEventsAsync(ParseResult args, CancellationToken ct)
    {
        Report(new JsonObject { ["stage"] = "started", ["operation"] = LauncherCommands.Operation(args) }, true);
        var result = await ExecuteCommandAsync(args, ct).ConfigureAwait(false);
        Report(new JsonObject { ["stage"] = "completed", ["operation"] = LauncherCommands.Operation(args) }, true);
        return result;
    }

    private void ReportClientProgress(LauncherProgress data)
    {
        // Coalesce before serialization: extraction can report several activities for every small file.
        if ((reportEvent is null && _activeJob.Value is null) || Environment.TickCount64 - Interlocked.Read(ref _lastProgress) < 250)
            return;
        Report(JsonSerializer.SerializeToNode(data, ApplicationJsonContext.Progress)!.AsObject());
    }

    private void Report(JsonObject data, bool force = false)
    {
        var job = _activeJob.Value;
        if (reportEvent is null && job is null)
            return;
        var now = Environment.TickCount64;
        if (!force && now - Interlocked.Read(ref _lastProgress) < 250)
            return;
        Interlocked.Exchange(ref _lastProgress, now);
        data["time"] = DateTimeOffset.UtcNow.ToString("O");
        data["event"] = "progress";
        reportEvent?.Invoke(data);
        if (job is not null && data["jobId"]?.ToString() != job)
        {
            var file = Path.Combine(paths.JobsDirectory, job + ".progress");
            using var lease = ResourceLease.AcquireAsync(resource: file).GetAwaiter().GetResult();
            JsonFiles.WriteAsync(file, data).GetAwaiter().GetResult();
        }
    }

    internal IProgress<DownloadProgress> DownloadEvents => new InlineProgress<DownloadProgress>(p => Report(new JsonObject { ["stage"] = "download", ["sourceUri"] = p.SourceUri, ["path"] = p.Path, ["bytes"] = p.Bytes, ["totalBytes"] = p.TotalBytes, ["reused"] = p.Reused }));

    private sealed class InlineProgress<T>(Action<T> report) : IProgress<T>
    {
        /// <summary>Forwards a progress value synchronously on the reporting thread.</summary>
        /// <param name="value">The progress value supplied by the operation.</param>
        public void Report(T value) => report(value);
    }

    private (int Id, string StartUtc) StartHost(Command command, string id)
    {
        using var process = StartHostProcess(command, id);
        return (process.Id, process.StartTime.ToUniversalTime().ToString("O"));
    }

    private Process StartHostProcess(Command command, string id)
    {
        var start = new ProcessStartInfo(monitorExecutable)
        {
            UseShellExecute = true,
            WindowStyle = ProcessWindowStyle.Hidden
        };
        foreach (var prefix in monitorPrefixArguments ?? [])
            start.ArgumentList.Add(prefix);
        foreach (var token in LauncherCommands.CommandPath(command))
            start.ArgumentList.Add(token);
        start.ArgumentList.Add(id);
        start.ArgumentList.Add(CliOptions.DataDirectory.Name);
        start.ArgumentList.Add(paths.Root);
        return Process.Start(start) ?? throw new IOException(S.ErrorHostStartFailed);
    }

    internal Task<JsonNode?> ExecuteCommandAsync(ParseResult args, CancellationToken ct) =>
        args.CommandResult.Command is LauncherCommand command
            ? command.Handler(this, args, ct)
            : throw new ArgumentException(S.ErrorTerminalHostCommand);
    private Task<JsonNode?> ExecuteJobAsync(JobDefinition definition, CancellationToken ct) =>
        ExecuteWithEventsAsync(CommandJobs.Parse(definition), ct);
}