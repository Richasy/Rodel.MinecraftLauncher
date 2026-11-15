// Copyright (c) Richasy. All rights reserved.

using System.Security.AccessControl;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Publishes a narrowly edited external configuration while preserving concurrent revisions.</summary>
internal static class AiMcpConfigFile
{
    internal static async Task EditAsync(string path, Func<byte[]?, byte[]> edit, CancellationToken ct)
    {
        var directory = Path.GetDirectoryName(path)!;
        Directory.CreateDirectory(directory);
        using var pinned = AiScopedFiles.OpenDirectory(directory);
        var before = ReadConfig(path);
        var after = edit(before);
        if (Equal(before, after)) return;
        var temporary = path + ".rodel-" + Guid.NewGuid().ToString("N");
        var backup = temporary + ".backup";
        try
        {
            const FileOptions options = FileOptions.Asynchronous | FileOptions.WriteThrough;
            var stream = before is null
                ? new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, options)
                : new FileInfo(temporary).Create(FileMode.CreateNew, FileSystemRights.Write, FileShare.None, 4096, options,
                    new FileInfo(path).GetAccessControl(AccessControlSections.Access));
            await using (stream.ConfigureAwait(false))
            {
                await stream.WriteAsync(after, ct).ConfigureAwait(false);
                await stream.FlushAsync(ct).ConfigureAwait(false);
                stream.Flush(flushToDisk: true);
            }

            ct.ThrowIfCancellationRequested();
            if (!Equal(ReadConfig(path), before)) throw ConfigChanged();
            if (before is null)
            {
                File.Move(temporary, path, overwrite: false);
                return;
            }

            try
            {
                File.Replace(temporary, path, backup);
            }
            catch (IOException) when (!File.Exists(path) && File.Exists(backup))
            {
                File.Move(backup, path);
                throw;
            }

            if (!Equal(ReadConfig(backup), before))
            {
                // The backup is the exact revision displaced by ReplaceFile. Restore it only
                // while our own update is visible, and retain any intervening revision.
                if (Equal(ReadConfig(path), after))
                {
                    var displaced = temporary + ".intervening";
                    File.Replace(backup, path, displaced);
                    if (Equal(ReadConfig(displaced), after))
                    {
                        File.Delete(displaced);
                        throw ConfigChanged();
                    }

                    backup = displaced;
                }

                throw new LauncherOperationException("AI_INTEGRATION_COMMIT_RACE",
                    "The client configuration changed during replacement. The displaced revision was preserved in a recovery backup.",
                    new JsonObject { ["backupPath"] = backup });
            }

            File.Delete(backup);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static byte[]? ReadConfig(string path)
    {
        if (!File.Exists(path)) return null;
        using var config = AiScopedFiles.Open(Path.GetDirectoryName(path)!, Path.GetFileName(path));
        var bytes = new byte[checked((int)config.Stream.Length)];
        config.Stream.ReadExactly(bytes);
        return bytes;
    }

    private static bool Equal(byte[]? left, byte[]? right)
        => left is null ? right is null : right is not null && left.AsSpan().SequenceEqual(right);

    internal static LauncherOperationException ConfigChanged()
        => new("AI_INTEGRATION_CLIENT_CHANGED", "The client MCP configuration changed during preparation. Inspect a fresh plan before retrying.");
}