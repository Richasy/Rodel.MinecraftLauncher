// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Reads launcher-owned packages and actual installed mod identities without network access or instance changes.</summary>
    public Task<BundledModCatalog> GetBundledModsAsync(string instanceId, CancellationToken ct = default) => GetBundledModsAsync(instanceId, null, ct);

    /// <summary>Describes built-in packages using an optional inventory already read for this instance's content view.</summary>
    /// <param name="instanceId">The instance whose actual runtime determines available packages.</param>
    /// <param name="inventory">A current read-only display snapshot, or null to scan. Review and installation always read and validate their own inventory.</param>
    /// <param name="ct">Cancels runtime, package and optional inventory reading.</param>
    /// <returns>The advisory catalog for the selected instance and supplied display snapshot.</returns>
    public async Task<BundledModCatalog> GetBundledModsAsync(string instanceId, IReadOnlyList<ContentEntry>? inventory, CancellationToken ct = default)
    {
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        var targets = await BundledModPackage.ReadCachedTargetsAsync(ct).ConfigureAwait(false);
        var target = MatchBundledTarget(context, targets);
        var packages = target is null ? [] : await BundledModPackage.ReadCachedAsync(target, ct).ConfigureAwait(false);
        inventory ??= await Content.ListAsync(Instances.Get(instanceId).GameDirectory, "mods", ct: ct).ConfigureAwait(false);
        var measuredInventory = inventory.ToArray();
        var bundledIds = packages.Select(package => package.Id).ToHashSet(StringComparer.Ordinal);
        for (var index = 0; index < measuredInventory.Length; index++)
        {
            var entry = measuredInventory[index];
            if (entry.Sha1 is null && ModId(entry) is { } id && bundledIds.Contains(id))
                measuredInventory[index] = await ContentService.InspectListedModAsync(Instances.Get(instanceId).GameDirectory, entry.FileName, ct).ConfigureAwait(false);
        }

        inventory = measuredInventory;
        return new()
        {
            InstanceId = instanceId,
            Context = context,
            Incompatibility = BundledIncompatibility(context, targets),
            MinecraftVersions = [.. targets.Where(target => target.Loader == context.Loader).Select(target => target.MinecraftVersion).Distinct()],
            RuntimeTargets = [.. targets.Select(target => target.MinecraftVersion + " / " + target.Loader)],
            Mods = [.. packages.Where(package => package.Id != "fabric-api").Select(package =>
            {
                var installed = inventory.Where(entry => ModId(entry) == package.Id).ToArray();
                return new BundledMod
                {
                    Id = package.Id,
                    Name = package.Name,
                    Version = package.Version,
                    MinecraftVersion = package.MinecraftVersion,
                    Loader = package.Loader,
                    InstalledVersion = installed.FirstOrDefault()?.Metadata?["version"]?.ToString(),
                    Installed = installed.Length == 1 && installed[0].Enabled && installed[0].Sha1 == package.Sha1,
                    Requires = [.. package.Dependencies.Select(item => item.Key).Where(id => id != "fabric-api" && packages.Any(candidate => candidate.Id == id))],
                };
            })],
        };
    }

    /// <summary>Freezes selected built-in packages, their bundled dependencies and existing files before installation.</summary>
    public async Task<ContentReview> ReviewBundledModsAsync(string instanceId, IReadOnlyList<string> modIds, CancellationToken ct = default)
    {
        EnsureInstanceIdle(instanceId);
        var context = await GetContentContextAsync(instanceId, ct: ct).ConfigureAwait(false);
        var targets = await BundledModPackage.ReadTargetsAsync(ct).ConfigureAwait(false);
        if (BundledIncompatibility(context, targets) is { } reason)
            throw new LauncherOperationException("BUNDLED_MOD_INCOMPATIBLE", "No built-in packages match this runtime.", new JsonObject { ["reason"] = reason });
        var target = MatchBundledTarget(context, targets)!;
        var minecraftVersion = target.MinecraftVersion;
        var packages = await BundledModPackage.ReadAsync(target, ct).ConfigureAwait(false);
        var selected = modIds.Distinct(StringComparer.Ordinal).ToArray();
        if (selected.Length == 0 || selected.Any(id => id == "fabric-api" || !packages.Any(package => package.Id == id)))
            throw new ArgumentException("Select one or more known built-in mods.", nameof(modIds));
        var game = Instances.Get(instanceId).GameDirectory;
        var fingerprint = await ContentInventoryFingerprintAsync(instanceId, ct).ConfigureAwait(false);
        var ordered = new List<BundledModPackage>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        void Include(string id)
        {
            if (!seen.Add(id)) return;
            var package = packages.Single(value => value.Id == id);
            if (package.Dependencies is { } dependencies)
            {
                foreach (var dependency in dependencies)
                    if (packages.Any(value => value.Id == dependency.Key)) Include(dependency.Key);
            }

            ordered.Add(package);
        }

        foreach (var id in selected) Include(id);
        var inventory = await Content.ListAsync(game, "mods", ct: ct).ConfigureAwait(false);
        bool KeepDependency(BundledModPackage package)
        {
            if (package.Id != "fabric-api") return false;
            var existing = inventory.Where(entry => ModId(entry) == package.Id).ToArray();
            return existing.Length == 1 && existing[0].Enabled
                && (existing[0].Metadata?["depends"]?["minecraft"] is not { } gameRange || BundledGameRangeMatches(minecraftVersion, gameRange))
                && ordered
                .Where(consumer => consumer.Dependencies[package.Id] is not null)
                .All(consumer => DependencyRangeMatches(existing[0].Metadata?["version"]?.ToString() ?? string.Empty, consumer.Dependencies[package.Id]) == true);
        }

        var nodes = ordered.Where(package => !KeepDependency(package)).Select(package => new ContentPlanNode
        {
            Platform = "builtin",
            ProjectId = package.Id,
            VersionId = package.Version,
            FileName = package.FileName,
            Kind = "mods",
            Source = package.Source,
            Sha1 = package.Sha1,
            Size = package.Size,
            GameVersion = minecraftVersion,
            Loader = target.Loader,
            Dependency = !selected.Contains(package.Id),
            DesiredEnabled = true,
            Title = package.Name,
            Dependencies = package.Dependencies.DeepClone(),
            Upstream = package.Metadata.DeepClone().AsObject(),
        }).ToArray();
        var plan = new ContentInstallPlan
        {
            Platform = "builtin",
            PrimaryProject = selected[0],
            PrimaryProjects = selected,
            GameDirectory = game,
            LockFingerprint = await ContentService.LockFingerprintAsync(game, ct).ConfigureAwait(false),
            Nodes = nodes,
            Edges = [],
            Conflicts = [],
            Warnings = [],
            CanApply = true,
        };
        var review = new ContentReview
        {
            Id = Guid.NewGuid().ToString("N"),
            InstanceId = instanceId,
            Mode = "builtin",
            GameDirectory = game,
            Context = context,
            Fingerprint = fingerprint,
            Plan = plan,
            CanApply = true,
        };
        await JsonFiles.WriteAsync(ContentReviewPath(review.Id), JsonSerializer.SerializeToNode(review, ContentJsonContext.Review)!, ct).ConfigureAwait(false);
        return review;
    }

    private async Task<ContentInstallReceipt> ApplyBundledModsAsync(ContentReview review, CancellationToken ct)
    {
        var plan = review.Plan ?? throw new InvalidDataException("The built-in selection is missing.");
        var targets = await BundledModPackage.ReadTargetsAsync(ct).ConfigureAwait(false);
        var target = MatchBundledTarget(review.Context, targets) ?? throw new InvalidDataException("The reviewed runtime is no longer bundled.");
        var actual = await GetContentContextAsync(review.InstanceId, ct: ct).ConfigureAwait(false);
        if (actual.MinecraftVersion != target.MinecraftVersion || actual.Loader != target.Loader)
            throw new LauncherOperationException("CONTENT_SOURCE_CHANGED", "The instance runtime changed; reopen the built-in mod selection.");
        var packages = await BundledModPackage.ReadAsync(target, ct).ConfigureAwait(false);
        var nodes = new List<ContentPlanNode>();
        foreach (var node in plan.Nodes)
        {
            var package = packages.SingleOrDefault(package => package.Id == node.ProjectId);
            if (package is null || node.Platform != "builtin" || node.Kind != "mods" || node.VersionId != package.Version || node.Sha1 != package.Sha1
                || node.Loader != target.Loader || node.GameVersion != target.MinecraftVersion)
            {
                throw new LauncherOperationException("CONTENT_SOURCE_CHANGED", "The bundled packages changed; reopen the built-in mod selection.");
            }

            nodes.Add(node with { Source = package.Source, FileName = package.FileName, DesiredEnabled = true });
        }

        var inventory = await Content.ListAsync(review.GameDirectory, "mods", ct: ct).ConfigureAwait(false);
        return await Content.ApplyGroupAsync(review.GameDirectory, nodes, plan.LockFingerprint, plan.PrimaryProject, ct, inventory).ConfigureAwait(false);
    }

    private static string? ModId(ContentEntry entry) => ContentService.GetModIdentity(entry.Metadata);
    private static bool BundledGameRangeMatches(string version, JsonNode range)
    {
        if (range is JsonArray alternatives) return alternatives.Any(item => item is not null && BundledGameRangeMatches(version, item));
        if (range is not JsonValue value || !value.TryGetValue<string>(out var text)) return false;
        // Fabric's bare trailing '-' includes prereleases of a stable numeric boundary.
        // This catalog selects stable releases only; named prereleases remain unsupported here.
        var stableRange = string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(token => token.TrimEnd('-')));
        return DependencyRangeMatches(version, JsonValue.Create(stableRange)) == true;
    }
    private static BundledModTarget? MatchBundledTarget(ContentCompatibility context, IReadOnlyList<BundledModTarget> targets)
        => targets.SingleOrDefault(target => target.MinecraftVersion == context.MinecraftVersion && target.Loader == context.Loader);

    private static string? BundledIncompatibility(ContentCompatibility context, IReadOnlyList<BundledModTarget> targets)
        => !targets.Any(target => target.Loader == context.Loader) ? "loader"
        : MatchBundledTarget(context, targets) is null ? "minecraft-version" : null;
}