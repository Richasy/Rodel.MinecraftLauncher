// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Packs;
using System.CommandLine;
using System.Text.Json;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class PackCommands
{

    internal PackCommands(Command root)
    {
        var pack = Group(root, "pack", S.GroupPackDescription);
        var forMultiplayer = new Option<bool>("--for-multiplayer") { Description = S.PackMultiplayerExport };
        Option[] optionalFiles = [O.OptionalPolicy, O.IncludeOptional, O.ExcludeOptional];
        Option[] packOptions = [O.ManualDirectory, .. optionalFiles, O.LibraryMirrors, O.Offline, O.ContentOnly, O.ServerOwner];
        Option[] onlineOptions = [O.Platform, O.Project, O.PackVersion, O.Channel, O.Plan, O.Offline, O.ContentOnly, O.LibraryMirrors, .. optionalFiles];
        Add(pack, "install", S.PackInstallDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.InstallOnlinePackAsync(result.GetRequiredValue(A.Target), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackResult), [A.Target], [O.Name, O.Platform, O.PackVersion, O.Channel, O.Plan, O.Offline, O.ContentOnly, O.LibraryMirrors, .. optionalFiles]);
        Add(pack, "update-online", S.PackUpdateOnlineDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.UpdateOnlinePackAsync(result.GetRequiredValue(A.Target), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackResult), [A.Target], onlineOptions);
        Add(pack, "review-online", S.PackReviewOnlineDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ReviewOnlinePackAsync(result.GetRequiredValue(A.Target), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackResult), [A.Target], onlineOptions)
            .TaskFactory = result => new ReviewOnlinePackTask(result.GetRequiredValue(A.Target), ParseOptions(result));
        Add(pack, "inspect", S.PackInspectDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Packs.Inspect(result.GetRequiredValue(A.Target)), PackJsonContext.Default.PackInspection)), [A.Target]);
        Add(pack, "stage-file", S.PackStageFile, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.Packs.StageManualFileAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Item), result.GetRequiredValue(A.Value), ct).ConfigureAwait(false), PackJsonContext.Default.PackManualFile), [A.Target, A.Item, A.Value]);
        Add(pack, "check", S.PackCheckDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.ReviewDistributionAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target])
            .TaskFactory = result => new ReviewPackDistributionTask(result.GetRequiredValue(A.Target));
        Add(pack, "import", S.PackImportDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ImportPackAsync(result.GetRequiredValue(A.Target), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackState), [A.Target], [O.Name, .. packOptions])
            .TaskFactory = result => new ImportPackTask(result.GetRequiredValue(A.Target), ParseOptions(result));
        Add(pack, "prepare", S.PackPrepareDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.PreparePackAsync(result.GetRequiredValue(A.Target), ParseOptions(result), result.GetValue(A.OptionalTarget), ct).ConfigureAwait(false), PackJsonContext.Default.PackState), [A.Target, A.OptionalTarget], [O.LibraryMirrors, O.ManualDirectory, O.Offline, O.ContentOnly])
            .TaskFactory = result => new PreparePackWorkflowTask(result.GetRequiredValue(A.Target), ParseOptions(result), result.GetValue(A.OptionalTarget));
        Add(pack, "complete", S.PackCompleteDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.CompleteAsync(result.GetRequiredValue(A.Target), result.GetValue(A.OptionalTarget), !result.GetValue(O.Offline), result.GetValue(O.ManualDirectory), ct).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target, A.OptionalTarget], [O.Offline, O.ManualDirectory])
            .TaskFactory = result => new CompletePackTask(result.GetRequiredValue(A.Target), result.GetValue(A.OptionalTarget), !result.GetValue(O.Offline), result.GetValue(O.ManualDirectory));
        Add(pack, "export", S.PackExportDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.ExportAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Destination), result.GetValue(O.Format) ?? "mrpack", result.GetValue(O.IncludeLocalMods), result.GetValue(forMultiplayer), ct).ConfigureAwait(false), PackJsonContext.Default.PackExportResult), [A.Target, A.Destination], [O.Format, O.IncludeLocalMods, forMultiplayer]);
        Add(pack, "review", S.PackReviewDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.ReviewAsync(instanceId: result.GetRequiredValue(A.Target), archivePath: result.GetRequiredValue(A.Destination), download: !result.GetValue(O.Offline), manualDirectory: result.GetValue(O.ManualDirectory), cancellationToken: ct, optionalSelection: ParseOptions(result).OptionalSelection).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target, A.Destination], [O.Offline, O.ManualDirectory, .. optionalFiles])
            .TaskFactory = result => new ReviewPackUpdateTask(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Destination), ParseOptions(result));
        Add(pack, "apply", S.PackApplyDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ApplyPackUpdateAsync(result.GetRequiredValue(A.Target), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target], [O.Offline, O.ContentOnly, O.LibraryMirrors])
            .TaskFactory = result => new ApplyPackUpdateTask(result.GetRequiredValue(A.Target), ParseOptions(result));
        Add(pack, "update", S.PackUpdateDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.UpdatePackAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Destination), ParseOptions(result), ct).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target, A.Destination], packOptions);
        Add(pack, "rollback", S.PackRollbackDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.RollbackAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), PackJsonContext.Default.PackReview), [A.Target])
            .TaskFactory = result => new RollbackPackUpdateTask(result.GetRequiredValue(A.Target));
        Add(pack, "history", S.PackHistoryDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Packs.GetOperations(result.GetRequiredValue(A.Target)), PackJsonContext.Default.PackOperationList)), [A.Target]);
        Add(pack, "detach", S.PackDetachDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Packs.DetachSourceAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), PackJsonContext.Default.PackState), [A.Target]);
    }

    private static PackWorkflowOptions ParseOptions(ParseResult args) => new()
    {
        Name = args.GetValue(O.Name),
        ServerId = args.GetValue(O.ServerOwner),
        ManualDirectory = args.GetValue(O.ManualDirectory),
        Platform = args.GetValue(O.Platform) ?? "modrinth",
        ProjectId = args.GetValue(O.Project),
        VersionId = args.GetValue(O.PackVersion),
        Channel = args.GetValue(O.Channel),
        OptionalSelection = args.GetValue(O.OptionalPolicy) is not null || (args.GetValue(O.IncludeOptional) ?? []).Length > 0 || (args.GetValue(O.ExcludeOptional) ?? []).Length > 0
            ? new PackOptionalSelection(args.GetValue(O.OptionalPolicy), [.. args.GetValue(O.IncludeOptional) ?? []], [.. args.GetValue(O.ExcludeOptional) ?? []])
            : null,
        LibraryMirrors = [.. args.GetValue(O.LibraryMirrors) ?? []],
        Offline = args.GetValue(O.Offline),
        PlanOnly = args.GetValue(O.Plan),
        ContentOnly = args.GetValue(O.ContentOnly)
    };
}