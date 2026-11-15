// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Backups;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class BackupCommands
{

    internal BackupCommands(Command root)
    {
        var backup = Group(root, "backup", S.GroupBackupDescription);
        Add(backup, "list", S.BackupListDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(new BackupService(app.Paths).List(result.GetValue(O.Instance)), BackupJsonContext.Default.IReadOnlyListBackupManifest)), options: [O.Instance]);
        Add(backup, "inspect", S.BackupInspectDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(new BackupService(app.Paths).Inspect(result.GetRequiredValue(A.Target)), BackupJsonContext.Default.BackupManifest)), [A.Target]);
        Add(backup, "create", S.BackupCreateDescription, async (app, result, ct) => await new BackupService(app.Paths).CreateAsync(result.GetRequiredValue(A.Target), result.GetValue(O.Scope) ?? "all", !result.GetValue(O.IncludeDownloads), ct).ConfigureAwait(false), BackupJsonContext.Default.BackupManifest, [A.Target], [O.Scope, O.IncludeDownloads]);
        Add(backup, "restore", S.BackupRestoreDescription, async (app, result, ct) => await new BackupService(app.Paths).RestoreAsync(result.GetRequiredValue(A.Target), result.GetValue(O.Instance), ct).ConfigureAwait(false), BackupJsonContext.Default.BackupRestoreResult, [A.Target], [O.Instance]);
        Add(backup, "rollback", S.BackupRollbackDescription, async (app, result, ct) => await new BackupService(app.Paths).RollbackAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), BackupJsonContext.Default.BackupRestoreState, [A.Target]);
        Add(backup, "delete", S.BackupDeleteDescription, async (app, result, ct) =>
        {
            var id = result.GetRequiredValue(A.Target);
            await new BackupService(app.Paths).DeleteAsync(id, ct).ConfigureAwait(false);
            return new JsonObject { ["deleted"] = id };
        }, [A.Target]);
    }
}