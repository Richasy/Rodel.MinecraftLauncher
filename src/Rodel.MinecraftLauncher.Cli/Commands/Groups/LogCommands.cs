// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class LogCommands
{
    internal LauncherCommand Read { get; }
    internal LauncherCommand Events { get; }

    internal LogCommands(Command root)
    {
        Option[] logOffsets = [O.Bytes, O.StdoutOffset, O.StderrOffset];
        Read = Add(root, "logs", S.LogLogsDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new LogService(app.Paths).ReadAsync(result.GetRequiredValue(A.LogSession), result.GetValue(O.Tail) ?? 100, result.GetValue(O.Bytes) ?? 65536, result.GetValue(O.StdoutOffset), result.GetValue(O.StderrOffset), ct).ConfigureAwait(false), LaunchJsonContext.Default.LogPreview), [A.LogSession], [.. logOffsets, O.Tail]);
        Read.ValidateInput = result =>
        {
            if (result.GetValue(A.LogSession) is null)
                throw new ArgumentException(S.ErrorSessionRequired);
        };
        Events = Add(Read, "events", S.LogEventsDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await ReadEventsAsync(app, result, ct).ConfigureAwait(false), LaunchJsonContext.Default.LogEventRead), [A.Target], [.. logOffsets, O.State, O.StateFile, O.Level]);
        Events.ValidateInput = result =>
        {
            if (result.GetValue(O.State) is not null && result.GetValue(O.StateFile) is not null)
                throw new ArgumentException(S.ErrorLogStateConflict);
        };
        var bundle = Add(Read, "bundle", S.LogBundleDescription, async (app, result, ct) => await new MaintenanceService(app.Paths).ExportBundleAsync(result.GetRequiredValue(A.Target), result.GetValue(O.Plan) ? null : result.GetRequiredValue(A.OptionalTarget), result.GetValue(O.Redact) ?? true, ct).ConfigureAwait(false), ApplicationJsonContext.Default.DiagnosticBundleManifest, [A.Target, A.OptionalTarget], [O.Plan, O.Redact]);
        bundle.ValidateInput = result =>
        {
            if (!result.GetValue(O.Plan) && result.GetValue(A.OptionalTarget) is null)
                throw new ArgumentException(S.ErrorBundleDestinationRequired);
        };
        Add(Read, "export", S.LogExportDescription, async (app, result, ct) => await new MaintenanceService(app.Paths).ExportLogsAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Destination), ct).ConfigureAwait(false), ApplicationJsonContext.Default.LogExportResult, [A.Target, A.Destination]);
        var launcher = Group(Read, "launcher", S.LogLauncherDescription);
        Add(launcher, "export", S.LogLauncherExportDescription, async (app, result, ct) => await new MaintenanceService(app.Paths).ExportLauncherLogsAsync(result.GetRequiredValue(A.Destination), ct).ConfigureAwait(false), ApplicationJsonContext.Default.LauncherLogExportResult, [A.Destination]).UsesCache = false;
    }

    private static Task<LogEventRead> ReadEventsAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var state = args.GetValue(O.State) is { } text ? JsonNode.Parse(text) as JsonObject : args.GetValue(O.StateFile) is { } file ? JsonFiles.ReadObject(file) : null;
        return new LogEventService(app.Paths).ReadAsync(args.GetRequiredValue(A.Target), args.GetValue(O.Bytes) ?? 65536, args.GetValue(O.StdoutOffset), args.GetValue(O.StderrOffset), state, args.GetValue(O.Level), ct);
    }
}