// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class MaintenanceCommands
{
    internal LauncherCommand Diagnose { get; }

    internal MaintenanceCommands(Command root)
    {
        Diagnose = Add(root, "diagnose", S.MaintenanceDiagnoseDescription, (app, result, ct) => DiagnoseAsync(app, result, ct), [A.OptionalTarget], [O.Verify, O.Session, O.Report, O.Encoding]);
        Diagnose.ValidateInput = result =>
        {
            var targets = (result.GetValue(A.OptionalTarget) is null ? 0 : 1) + (result.GetValue(O.Session) is null ? 0 : 1) + (result.GetValue(O.Report) is null ? 0 : 1);
            if (targets != 1)
                throw new ArgumentException(S.ErrorDiagnosticTargetRequired);
        };
        var cache = Group(root, "cache", S.GroupCacheDescription);
        Add(cache, "status", S.MaintenanceStatusDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new CacheService(app.Paths).StatusAsync(ct).ConfigureAwait(false), ApplicationJsonContext.Default.CacheStatus)).UsesCache = false;
        Add(cache, "prune", S.MaintenancePruneDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new MaintenanceService(app.Paths).PruneCacheAsync(result.GetValue(O.OlderThanDays) ?? 30, result.GetValue(O.Plan), ct).ConfigureAwait(false), ApplicationJsonContext.Default.CachePruneResult), options: [O.OlderThanDays, O.Plan]).UsesCache = false;
    }

    private static async Task<JsonNode?> DiagnoseAsync(LauncherApplication app, ParseResult args, CancellationToken ct) =>
        args.GetValue(O.Report) is { } report
            ? JsonSerializer.SerializeToNode(await CrashReportService.AnalyzeAsync(file: report, ct: ct, encoding: args.GetValue(O.Encoding)).ConfigureAwait(false), ApplicationJsonContext.Default.CrashAnalysis)
            : args.GetValue(O.Session) is { } session
                ? JsonSerializer.SerializeToNode(await new MaintenanceService(app.Paths).DiagnoseSessionAsync(session, ct).ConfigureAwait(false), ApplicationJsonContext.Default.SessionDiagnosis)
                : JsonSerializer.SerializeToNode(await new MaintenanceService(app.Paths).DiagnoseAsync(args.GetRequiredValue(A.OptionalTarget), args.GetValue(O.Verify), ct).ConfigureAwait(false), ApplicationJsonContext.Default.InstanceDiagnosis);
}