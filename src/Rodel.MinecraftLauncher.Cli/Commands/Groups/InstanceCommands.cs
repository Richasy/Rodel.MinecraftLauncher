// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class InstanceCommands
{
    internal LauncherCommand List { get; }
    internal LauncherCommand Show { get; }
    internal LauncherCommand Create { get; }
    internal LauncherCommand ContentContext { get; }

    internal InstanceCommands(Command root)
    {
        var instance = Group(root, "instance", S.GroupInstanceDescription);
        List = Add(instance, "list", S.InstanceListDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.ListInstances(), InstanceJsonContext.Default.IReadOnlyListInstanceSummary)));
        Show = Add(instance, "show", S.InstanceShowDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Instances.Get(result.GetRequiredValue(A.Instance)).Describe(), InstanceJsonContext.Default.InstanceSummary)), [A.Instance]);
        ContentContext = Add(instance, "content-context", S.InstanceContentContextDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.GetContentContextAsync(result.GetRequiredValue(A.Instance), ct: ct).ConfigureAwait(false), ContentJsonContext.Default.ContentCompatibility), [A.Instance]);
        Add(instance, "prepare", S.InstancePrepareDescription, async (app, result, ct) => await app.Client.Runtimes.PrepareMigrationAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), RuntimeJsonContext.Default.RuntimePreparationResult, [A.Instance])
            .TaskFactory = result => new PrepareMigrationTask(result.GetRequiredValue(A.Instance));
        Add(instance, "remove", S.InstanceRemoveDescription, async (app, result, ct) =>
        {
            var id = result.GetRequiredValue(A.Instance);
            await app.Instances.RemoveAsync(id, result.GetValue(O.DeleteFiles), ct).ConfigureAwait(false);
            return new JsonObject { ["removed"] = id };
        }, [A.Instance], [O.DeleteFiles]);
        Create = Add(instance, "create", S.InstanceCreateDescription, async (app, result, ct) =>
            (await app.Instances.CreateAsync(result.GetRequiredValue(A.Name), result.GetValue(O.Version) ?? throw new ArgumentException(S.ErrorVersionRequired), ct).ConfigureAwait(false)).ToJson(), [A.Name], [O.Version]);
        Required(Create, O.Version);
        Option[] importOptions = [O.Version, O.GameDirectory, O.MinecraftDirectory, O.Profile, O.LauncherConfig];
        Add(instance, "inspect-links", S.InstanceInspectLinksDescription, (app, result, ct) =>
            Task.FromResult(JsonSerializer.SerializeToNode(app.Instances.PreviewLinks(result.GetRequiredValue(A.Target)), InstanceJsonContext.Default.IReadOnlyListInstanceImportPreview)), [A.Target]);
        Add(instance, "link", S.InstanceLinkDescription, async (app, result, ct) =>
        {
            var version = result.GetValue(O.Version);
            if (version is null && (result.GetValue(O.GameDirectory) is not null || result.GetValue(O.Profile) is not null))
                throw new ArgumentException(S.ErrorVersionRequired);
            return JsonSerializer.SerializeToNode(await app.Instances.LinkExistingAsync(result.GetRequiredValue(A.Target),
                version is not null ? [new(version, result.GetValue(O.GameDirectory), result.GetValue(O.Profile))] : null, ct).ConfigureAwait(false),
                InstanceJsonContext.Default.IReadOnlyListInstanceSummary);
        }, [A.Target], [O.Version, O.GameDirectory, O.Profile]);
        Add(instance, "inspect-import", S.InstanceInspectImportDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Instances.PreviewImport(result.GetRequiredValue(A.Target), result.GetValue(O.Profile), result.GetValue(O.MinecraftDirectory), result.GetValue(O.LauncherConfig), result.GetValue(O.Version), result.GetValue(O.GameDirectory)), InstanceJsonContext.Default.InstanceImportPreview)), [A.Target], importOptions);
        Required(Add(instance, "import", S.InstanceImportDescription, async (app, result, ct) => await app.Client.ImportInstanceAsync(result.GetRequiredValue(A.Name), result.GetValue(O.Directory) ?? throw new ArgumentException(S.ErrorDirectoryRequired), new InstanceImportOptions
        {
            ServerId = result.GetValue(O.ServerOwner),
            VersionId = result.GetValue(O.Version),
            GameDirectory = result.GetValue(O.GameDirectory),
            Profile = result.GetValue(O.Profile),
            MinecraftDirectory = result.GetValue(O.MinecraftDirectory),
            LauncherConfiguration = result.GetValue(O.LauncherConfig),
            PlanOnly = result.GetValue(O.Plan),
            Migrate = result.GetValue(O.Migrate),
            Copy = result.GetValue(O.Copy)
        }, ct).ConfigureAwait(false), InstanceJsonContext.Default.IInstanceImportResult, [A.Name], [.. importOptions, O.Directory, O.Migrate, O.Copy, O.Plan, O.ServerOwner]), O.Directory);
        Add(instance, "rename", S.InstanceRenameDescription, async (app, result, ct) => (await app.Instances.RenameAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Destination), ct).ConfigureAwait(false)).ToJson(), [A.Instance, A.Destination]);
        Add(instance, "relink", S.InstanceRelinkDescription, async (app, result, ct) => (await app.Instances.RelinkAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Destination), result.GetValue(O.MinecraftDirectory), ct).ConfigureAwait(false)).ToJson(), [A.Instance, A.Destination], [O.MinecraftDirectory]);
        Add(instance, "copy", S.InstanceCopyDescription, async (app, result, ct) => (await app.Instances.CopyAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Destination), ct).ConfigureAwait(false)).ToJson(), [A.Instance, A.Destination]);
        Add(instance, "move", S.InstanceMoveDescription, async (app, result, ct) => (await app.Instances.MoveAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Destination), ct).ConfigureAwait(false)).ToJson(), [A.Instance, A.Destination]);
    }
}