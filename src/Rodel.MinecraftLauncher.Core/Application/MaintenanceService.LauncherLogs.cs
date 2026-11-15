// Copyright (c) Richasy. All rights reserved.

using System.Globalization;
using System.IO.Compression;
using System.Text.Json;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>Describes the bounded launcher-only archive, without game output or private configuration.</summary>
/// <param name="CreatedAt">Snapshot timestamp in UTC.</param>
/// <param name="RetentionDays">Number of UTC calendar days considered, including today.</param>
/// <param name="Files">Archive-relative activity file names actually included.</param>
/// <param name="Warnings">Missing, oversized or unreadable source observations.</param>
public sealed record LauncherLogManifest(string CreatedAt, int RetentionDays, IReadOnlyList<string> Files, IReadOnlyList<string> Warnings);

/// <summary>Reports a launcher-only log ZIP published without requiring a game session.</summary>
/// <param name="Path">Published archive path.</param>
/// <param name="Bytes">Published archive size.</param>
/// <param name="Manifest">Actual source inventory and read limitations, also saved inside the ZIP.</param>
public sealed record LauncherLogExportResult(string Path, long Bytes, LauncherLogManifest Manifest);

public sealed partial class MaintenanceService
{
    /// <summary>Exports the launcher's retained seven-day activity files with best-effort credential redaction, excluding game sessions, game logs and configuration. Existing destinations are preserved.</summary>
    public async Task<LauncherLogExportResult> ExportLauncherLogsAsync(string destination, CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        destination = Path.GetFullPath(destination);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        var files = new List<string>();
        var warnings = new List<string>();
        var root = Path.Combine(paths.LogsDirectory, "launcher");
        var today = DateTime.UtcNow.Date;
        string[] sources = [];
        try
        {
            if (Directory.Exists(root))
            {
                sources = [.. Directory.EnumerateFiles(root, "launcher-*.jsonl*", new EnumerationOptions { AttributesToSkip = FileAttributes.ReparsePoint, IgnoreInaccessible = false })
                    .Where(file => IsRetainedLauncherLog(Path.GetFileName(file), today)).Order(StringComparer.Ordinal)];
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            warnings.Add("LAUNCHER_LOG_DIRECTORY_UNREADABLE");
        }

        try
        {
            LauncherLogManifest manifest;
            using (var zip = ZipFile.Open(temporary, ZipArchiveMode.Create))
            {
                foreach (var source in sources)
                {
                    ct.ThrowIfCancellationRequested();
                    byte[] bytes;
                    try
                    {
                        var input = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
                        await using var inputDisposal = input.ConfigureAwait(false);
                        // Normal segments rotate at 8 MiB. Bound a damaged/manual source and snapshot its original
                        // length so a concurrently appending writer cannot keep this export open indefinitely.
                        if (input.Length > 16 * 1024 * 1024)
                        {
                            warnings.Add("LAUNCHER_LOG_TOO_LARGE:" + Path.GetFileName(source));
                            continue;
                        }

                        bytes = new byte[checked((int)input.Length)];
                        await input.ReadExactlyAsync(bytes, ct).ConfigureAwait(false);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                    {
                        warnings.Add("LAUNCHER_LOG_UNREADABLE:" + Path.GetFileName(source));
                        continue;
                    }

                    var entry = "launcher/" + Path.GetFileName(source);
                    using var snapshot = new MemoryStream(bytes, writable: false);
                    using var reader = new StreamReader(snapshot);
                    var writer = new StreamWriter(zip.CreateEntry(entry, CompressionLevel.Fastest).Open());
                    await using var writerDisposal = writer.ConfigureAwait(false);
                    await RedactStreamAsync(reader, writer, ct).ConfigureAwait(false);
                    files.Add(entry);
                }

                if (files.Count == 0) warnings.Add("LAUNCHER_LOGS_EMPTY");
                manifest = new(DateTimeOffset.UtcNow.ToString("O"), 7, files, warnings);
                var output = zip.CreateEntry("manifest.json", CompressionLevel.Fastest).Open();
                await using var outputDisposal = output.ConfigureAwait(false);
                await JsonSerializer.SerializeAsync(output, manifest, ApplicationJsonContext.Default.LauncherLogManifest, ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination, false);
            return new(destination, new FileInfo(destination).Length, manifest);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private static bool IsRetainedLauncherLog(string name, DateTime today) => name.Length >= 23
        && name.StartsWith("launcher-", StringComparison.Ordinal)
        && name[17..] is ".jsonl" or ".jsonl.1" or ".jsonl.2" or ".jsonl.3"
        && DateTime.TryParseExact(name.AsSpan(9, 8), "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var day)
        && day >= today.AddDays(-6) && day <= today;
}