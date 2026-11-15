// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ContentCommands
{
    private static readonly Option<string[]> SourceSelections = new("--select-source")
    {
        Description = S.OptionSourceSelectionsDescription,
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = false
    };
    internal LauncherCommand Search { get; }
    internal LauncherCommand List { get; }
    internal LauncherCommand Install { get; }
    internal LauncherCommand ReviewUpdates { get; }
    internal LauncherCommand ApplyReview { get; }
    internal LauncherCommand ReviewInstall { get; }
    internal LauncherCommand ShowReview { get; }

    internal ContentCommands(Command root)
    {
        var content = Group(root, "content", S.GroupContentDescription);
        Option[] localContent = [O.Kind, O.World];
        Option[] contentInstall = [.. localContent, O.Platform, O.ContentVersion, O.Minecraft, O.Loader, O.Channel, O.VersionSelections, O.NoDependencies, O.ExpectedLockFingerprint];
        Option[] contentUpdate = [.. localContent, O.Minecraft, O.Loader, O.Channel, O.VersionSelections, SourceSelections, O.NoDependencies, O.ExpectedLockFingerprint, O.UpdateContext];
        Search = Add(content, "search", S.ContentSearchDescription, async (app, result, ct) => await app.Client.ContentPlatform.SearchAsync(platform: result.GetValue(O.Platform) ?? "modrinth", query: result.GetRequiredValue(A.Target), projectType: result.GetValue(O.Type) ?? "all", ct: ct, offset: result.GetValue(O.Offset) ?? 0, limit: result.GetValue(O.Limit) ?? 20, minecraftVersion: result.GetValue(O.Minecraft), loader: result.GetValue(O.Loader), index: result.GetValue(O.Index) ?? "relevance").ConfigureAwait(false), [A.Target], [O.Platform, O.Type, O.Minecraft, O.Loader, O.Offset, O.Limit, O.Index]);
        Add(content, "project", S.ContentProjectDescription, async (app, result, ct) => await app.Client.ContentPlatform.GetProjectAsync(result.GetValue(O.Platform) ?? "modrinth", result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), [A.Target], [O.Platform]);
        Add(content, "versions", S.ContentVersionsDescription, async (app, result, ct) => await app.Client.ContentPlatform.GetVersionsAsync(result.GetValue(O.Platform) ?? "modrinth", result.GetRequiredValue(A.Target), result.GetValue(O.Minecraft), result.GetValue(O.Loader), ct).ConfigureAwait(false), [A.Target], [O.Platform, O.Minecraft, O.Loader]);
        List = Add(content, "list", S.ContentListDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ListContentAsync(result.GetRequiredValue(A.Instance), ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Entries), [A.Instance], localContent);
        Add(content, "bundled", S.ContentBundledDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(
            await app.Client.GetBundledModsAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), ContentJsonContext.Default.BundledModCatalog), [A.Instance]);
        Add(content, "review-bundled", S.ContentReviewBundledDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(
            await app.Client.ReviewBundledModsAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], ct).ConfigureAwait(false), ContentJsonContext.Review), [A.Instance, A.Items]);
        Add(content, "recover", S.ContentRecoverDescription, async (app, result, ct) => await app.Client.RecoverContentAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), [A.Instance])
            .TaskFactory = result => new RecoverContentTask(result.GetRequiredValue(A.Instance));
        Add(content, "analyze", S.ContentAnalyzeDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.AnalyzeContentAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), ContentJsonContext.Analysis), [A.Instance]);
        Option[] policyOptions = [.. localContent, O.Channel];
        Add(content, "pin", S.ContentPinDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SetContentPolicyAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), true, ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], policyOptions);
        Add(content, "unpin", S.ContentUnpinDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SetContentPolicyAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), false, ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], policyOptions);
        Add(content, "policy", S.ContentPolicyDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SetContentPolicyAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), null, ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], policyOptions);
        Add(content, "detach", S.ContentDetachDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.DetachContentAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], localContent);
        Add(content, "identify", S.ContentIdentifyDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.IdentifyContentAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], [.. localContent, O.ContentVersion, O.Minecraft, O.Loader, O.Algorithm, O.Attach]);
        Install = Add(content, "install", S.ContentInstallDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.InstallContentAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], [.. contentInstall, O.Plan]);
        Install.TaskFactory = result => new InstallContentTask(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ParseOptions(result));
        ReviewInstall = Add(content, "review-install", S.ContentReviewInstallDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ReviewContentInstallAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Review), [A.Instance, A.Item], contentInstall);
        ShowReview = Add(content, "show-review", S.ContentShowReviewDescription, async (app, result, ct) =>
            JsonSerializer.SerializeToNode(await app.Client.GetContentReviewAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ct).ConfigureAwait(false), ContentJsonContext.Review), [A.Instance, A.Item]);
        Add(content, "review-files", S.ContentReviewFilesDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await ReviewFilesAsync(app, result, ct).ConfigureAwait(false), ContentJsonContext.Review), [A.Instance, A.Items], [O.Kind]);
        Add(content, "add", S.ContentAddDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.AddContentAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Items], localContent);
        Add(content, "remove", S.ContentRemoveDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.RemoveContentAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Items], localContent);
        Add(content, "enable", S.ContentEnableDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SetContentEnabledAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], true, ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Items], localContent);
        Add(content, "disable", S.ContentDisableDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.SetContentEnabledAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], false, ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Items], localContent);
        Add(content, "update", S.ContentUpdateDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.UpdateContentAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Items], [.. contentUpdate, O.Plan]);
        ReviewUpdates = Add(content, "review-updates", S.ContentReviewUpdatesDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ReviewContentUpdatesAsync(result.GetRequiredValue(A.Instance), result.GetValue(A.Items) ?? [], ParseOptions(result), ct).ConfigureAwait(false), ContentJsonContext.Review), [A.Instance, A.Items], contentUpdate);
        ApplyReview = Confirm(Add(content, "apply-review", S.ContentApplyReviewDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.ApplyContentReviewAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item), ct).ConfigureAwait(false), ContentJsonContext.Result), [A.Instance, A.Item], [O.Confirm]));
        ApplyReview.TaskFactory = result => new ApplyContentReviewTask(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item));
        var dependencyRepair = Add(content, "repair", S.ContentRepairDescription, (app, result, ct) => RepairDependenciesAsync(app, result, ct), [A.Instance], [O.VersionSelections, O.Plan, O.Review, O.Session, O.Confirm]);
        dependencyRepair.ValidateInput = result =>
        {
            if (result.GetValue(O.Review) is not null)
            {
                if (!result.GetValue(O.Confirm))
                    throw new ArgumentException(S.ErrorRepairConfirmationRequired);
                if (result.GetValue(O.Plan) || result.GetValue(O.Session) is not null || (result.GetValue(O.VersionSelections) ?? []).Length > 0)
                    throw new ArgumentException(S.ErrorRepairReviewConflict);
            }
            else if (result.GetValue(O.Confirm) || result.GetValue(O.Session) is null)
            {
                throw new ArgumentException(S.ErrorRepairTargetRequired);
            }
        };
    }

    private static Task<ContentReview> ReviewFilesAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var items = args.GetValue(A.Items) ?? [];
        return app.Client.ReviewContentFilesAsync(args.GetRequiredValue(A.Instance), items,
            args.GetValue(O.Kind) is { } kind ? items.ToDictionary(Path.GetFullPath, _ => kind, StringComparer.OrdinalIgnoreCase) : null, ct);
    }

    private static async Task<JsonNode?> RepairDependenciesAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var instanceId = args.GetRequiredValue(A.Instance);
        if (args.GetValue(O.Review) is { } review)
        {
            if (!args.GetValue(O.Confirm))
                throw new ArgumentException(S.ErrorRepairConfirmationRequired);
            if (args.GetValue(O.Plan) || args.GetValue(O.Session) is not null || (args.GetValue(O.VersionSelections) ?? []).Length > 0)
                throw new ArgumentException(S.ErrorRepairReviewConflict);
            return JsonSerializer.SerializeToNode<ContentResult>(await app.Client.ApplyDependencyRepairAsync(instanceId, review, ct).ConfigureAwait(false), ContentJsonContext.Result);
        }

        if (args.GetValue(O.Confirm))
            throw new ArgumentException(S.ErrorRepairReviewRequired);
        return JsonSerializer.SerializeToNode(await app.Client.PlanDependencyRepairAsync(instanceId,
            args.GetValue(O.Session) ?? throw new ArgumentException(S.ErrorRepairSessionRequired),
            ParseOptions(args).InstallOptions.VersionSelections, ct).ConfigureAwait(false), ContentJsonContext.DependencyReview);
    }

    private static ContentWorkflowOptions ParseOptions(ParseResult args)
    {
        var sources = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in args.GetValue(SourceSelections) ?? [])
        {
            var separator = raw.LastIndexOf('=');
            if (separator <= 0 || separator == raw.Length - 1 || string.IsNullOrWhiteSpace(raw[..separator]) || string.IsNullOrWhiteSpace(raw[(separator + 1)..]))
                throw new ArgumentException(S.ErrorSourceSelection);
            sources[raw[..separator]] = raw[(separator + 1)..];
        }

        var selections = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in args.GetValue(O.VersionSelections) ?? [])
        {
            var pair = raw.Split('=', 2);
            if (pair.Length != 2 || string.IsNullOrWhiteSpace(pair[0]) || string.IsNullOrWhiteSpace(pair[1]))
                throw new ArgumentException(S.ErrorVersionSelection);
            selections[pair[0]] = pair[1];
        }

        var source = args.GetValue(O.UpdateContext) switch
        {
            null or "current" => false,
            "source" => true,
            _ => throw new ArgumentException(S.ErrorUpdateContext)
        };
        return new ContentWorkflowOptions
        {
            Kind = args.GetValue(O.Kind) ?? "mods",
            World = args.GetValue(O.World),
            Platform = args.GetValue(O.Platform) ?? "modrinth",
            VersionId = args.GetValue(O.ContentVersion),
            MinecraftVersion = args.GetValue(O.Minecraft),
            Loader = args.GetValue(O.Loader),
            Algorithm = args.GetValue(O.Algorithm) ?? "sha1",
            Attach = args.GetValue(O.Attach),
            PlanOnly = args.GetValue(O.Plan),
            UseSourceContext = source,
            InstallOptions = new ContentInstallOptions
            {
                VersionSelections = selections,
                SourceSelections = sources,
                Channel = args.GetValue(O.Channel),
                IncludeDependencies = !args.GetValue(O.NoDependencies),
                ExpectedLockFingerprint = args.GetValue(O.ExpectedLockFingerprint)
            }
        };
    }
}