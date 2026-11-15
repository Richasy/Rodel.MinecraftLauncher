// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Worlds;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class WorldCommands
{

    internal WorldCommands(Command root)
    {
        var world = Group(root, "world", S.GroupWorldDescription);
        _ = new SeedExplorerCommands(world);
        _ = new WorldMapCommands(world);
        Add(world, "list", S.WorldListDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Worlds.ListAsync(result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), WorldJsonContext.Default.IReadOnlyListWorldSummary), [A.Instance]);
        Add(world, "directory", S.WorldDirectoryDescription, (app, result, ct) => Task.FromResult<JsonNode?>(JsonValue.Create(app.Client.Worlds.GetDirectory(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item)))), [A.Instance, A.Item]);
        Add(world, "import", S.WorldImportDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Import, result.GetValue(O.Name), result.GetRequiredValue(A.Item), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item], [O.Name]);
        Add(world, "export", S.WorldExportDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Export, result.GetRequiredValue(A.Item), destination: result.GetRequiredValue(A.Destination), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item, A.Destination]);
        Add(world, "backup", S.WorldBackupDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Backup, result.GetRequiredValue(A.Item), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item]);
        Add(world, "backups", S.WorldBackupsDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Worlds.ListBackups(result.GetRequiredValue(A.Instance), result.GetValue(O.World)), WorldJsonContext.Default.IReadOnlyListWorldArchiveRecord)), [A.Instance], [O.World]);
        Confirm(Add(world, "restore", S.WorldRestoreDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Restore, result.GetRequiredValue(A.Item), result.GetRequiredValue(A.Destination), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item, A.Destination], [O.Confirm]));
        Confirm(Add(world, "remove", S.WorldRemoveDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Remove, result.GetRequiredValue(A.Item), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item], [O.Confirm]));
        Add(world, "recovery", S.WorldRecoveryDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Worlds.ListRecovery(result.GetRequiredValue(A.Instance)), WorldJsonContext.Default.IReadOnlyListWorldArchiveRecord)), [A.Instance]);
        Confirm(Add(world, "recover", S.WorldRecoverDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Recover, result.GetValue(O.Name), source: result.GetRequiredValue(A.Item), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item], [O.Name, O.Confirm]));
        Add(world, "compress", S.WorldCompressDescription, async (app, result, ct) => await app.Client.ManageWorldAsync(result.GetRequiredValue(A.Instance), WorldAction.Compress, source: result.GetRequiredValue(A.Item), cancellationToken: ct).ConfigureAwait(false), WorldJsonContext.Default.WorldOperationResult, [A.Instance, A.Item]);
    }
}