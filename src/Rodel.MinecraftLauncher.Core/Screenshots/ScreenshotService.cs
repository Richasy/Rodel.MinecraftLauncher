// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Runtime.InteropServices;

namespace Rodel.MinecraftLauncher.Core.Screenshots;

/// <summary>Manages independent instance screenshots without reading worlds, requiring a game bridge or stopping the game.</summary>
/// <param name="paths">The launcher data root owning the instance registry.</param>
public sealed class ScreenshotService(LauncherPaths paths)
{
    /// <summary>Resolves the instance's actual screenshot directory without creating it.</summary>
    /// <param name="instanceId">The registered instance.</param>
    /// <returns>The absolute screenshot directory.</returns>
    public string GetDirectory(string instanceId) =>
        Path.GetFullPath(Path.Combine(new InstanceStore(paths).Get(instanceId).GameDirectory, "screenshots"));

    /// <summary>Creates the screenshot directory for an explicit open-folder action, without changing game files.</summary>
    /// <param name="instanceId">The registered instance.</param>
    /// <returns>The existing or newly created directory.</returns>
    public string EnsureDirectory(string instanceId) => Directory.CreateDirectory(GetDirectory(instanceId)).FullName;

    /// <summary>Reads one image's current file metadata; no image pixels are decoded.</summary>
    /// <param name="instanceId">The registered instance.</param>
    /// <param name="fileName">A single supported image file name, not a path.</param>
    /// <returns>The current image selection.</returns>
    public ScreenshotFile GetFile(string instanceId, string fileName) => ReadFile(GetDirectory(instanceId), fileName);

    /// <summary>Revalidates a displayed selection before a desktop open or clipboard action uses its path.</summary>
    /// <param name="instanceId">The registered instance that owns the selection.</param>
    /// <param name="selection">The file path, length and last-write time originally displayed.</param>
    /// <returns>The current file when its location and observed metadata still match.</returns>
    public ScreenshotFile GetFile(string instanceId, ScreenshotFile selection) => ValidateSelection(GetDirectory(instanceId), selection);

    /// <summary>Enumerates local screenshots off the caller's thread, preserving individual read failures.</summary>
    /// <param name="instanceId">The registered instance.</param>
    /// <param name="cancellationToken">Cancels enumeration without creating or changing files.</param>
    /// <returns>The image inventory, including an empty result for a not-yet-created screenshot directory.</returns>
    public Task<ScreenshotCatalog> ListAsync(string instanceId, CancellationToken cancellationToken = default) => Task.Run(() =>
    {
        var directory = GetDirectory(instanceId);
        var files = new List<ScreenshotFile>();
        var warnings = new List<ScreenshotWarning>();
        try
        {
            foreach (var path in Directory.EnumerateFiles(directory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!IsImage(path)) continue;
                var name = Path.GetFileName(path);
                try { files.Add(ReadFile(directory, name)); }
                catch (Exception error) when (IsFileError(error)) { warnings.Add(new(name, error.Message)); }
            }
        }
        catch (DirectoryNotFoundException) when (!File.Exists(directory))
        {
            return new ScreenshotCatalog(directory, false, [], []);
        }

        return new ScreenshotCatalog(directory, true,
            [.. files.OrderByDescending(file => file.ModifiedUtc).ThenBy(file => file.FileName, StringComparer.OrdinalIgnoreCase)], warnings);
    }, cancellationToken);

    /// <summary>Renames the selected image without overwriting another file or changing its encoded format.</summary>
    /// <param name="instanceId">The registered instance owning the selection.</param>
    /// <param name="file">The metadata observed when the user selected the image.</param>
    /// <param name="newName">The new file name; an omitted extension retains the original extension.</param>
    /// <param name="cancellationToken">Cancels before the rename is committed.</param>
    /// <returns>The renamed file's actual metadata.</returns>
    public Task<ScreenshotFile> RenameAsync(string instanceId, ScreenshotFile file, string newName, CancellationToken cancellationToken = default) => Task.Run(async () =>
    {
        var directory = GetDirectory(instanceId);
        using var lease = await ResourceLease.AcquireAsync(directory, cancellationToken).ConfigureAwait(false);
        var current = ValidateSelection(directory, file);
        ArgumentException.ThrowIfNullOrWhiteSpace(newName);
        var extension = Path.GetExtension(current.FileName);
        if (Path.GetExtension(newName).Length == 0) newName += extension;
        ValidateName(newName);
        if (!Path.GetExtension(newName).Equals(extension, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Renaming a screenshot must preserve its image extension.", nameof(newName));
        if (newName == current.FileName) return current;
        var destination = Path.Combine(directory, newName);
        if (!newName.Equals(current.FileName, StringComparison.OrdinalIgnoreCase) && File.Exists(destination))
            throw new LauncherOperationException("SCREENSHOT_DESTINATION_EXISTS", "Another screenshot already has this file name.");
        cancellationToken.ThrowIfCancellationRequested();
        File.Move(current.Path, destination);
        return ReadFile(directory, newName);
    }, cancellationToken);

    /// <summary>Exports exact image copies without overwriting existing destinations; successful files survive later failures or cancellation.</summary>
    /// <param name="instanceId">The registered instance owning every selection.</param>
    /// <param name="files">The selected file snapshots, never an implicit select-all operation.</param>
    /// <param name="destination">The explicitly chosen output directory.</param>
    /// <param name="cancellationToken">Stops the current staged copy and subsequent files.</param>
    /// <returns>Per-file results and any cancellation, preserving partial completion evidence.</returns>
    public Task<ScreenshotBatchResult> ExportAsync(string instanceId, IReadOnlyList<ScreenshotFile> files, string destination, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(destination);
        var output = Path.GetFullPath(destination);
        return RunBatchAsync(instanceId, files, async file =>
        {
            var target = Path.Combine(output, file.FileName);
            if (File.Exists(target) || Directory.Exists(target))
                throw new LauncherOperationException("SCREENSHOT_DESTINATION_EXISTS", "The export destination already contains this file.");
            var source = new FileStream(file.Path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            await using (source.ConfigureAwait(false))
            {
                ValidateSelection(GetDirectory(instanceId), file);
                await AtomicFile.CopyAsync(source, target, 65536, FileOptions.Asynchronous, cancellationToken, overwrite: false).ConfigureAwait(false);
            }

            return target;
        }, cancellationToken);
    }

    /// <summary>Moves selected images into the Windows recycle bin, rejecting permanent-delete fallback.</summary>
    /// <param name="instanceId">The registered instance owning every selection.</param>
    /// <param name="files">The selected file snapshots.</param>
    /// <param name="cancellationToken">Cancels before a file's effect or stops subsequent files; completed recycling is retained.</param>
    /// <returns>Per-file recycle receipts and failures, including partial completion.</returns>
    public Task<ScreenshotBatchResult> RecycleAsync(string instanceId, IReadOnlyList<ScreenshotFile> files, CancellationToken cancellationToken = default) =>
        RunBatchAsync(instanceId, files, async file =>
        {
            if (!OperatingSystem.IsWindows())
                throw new PlatformNotSupportedException("Screenshot recycling requires Windows.");
            return await WindowsRecycleBin.RecycleAsync(file.Path, () => ValidateSelection(GetDirectory(instanceId), file), cancellationToken).ConfigureAwait(false);
        }, cancellationToken);

    /// <summary>Permanently deletes explicitly selected images without creating a recycle-bin copy or restoring completed deletions.</summary>
    /// <param name="instanceId">The registered instance owning every selection.</param>
    /// <param name="files">The exact file snapshots whose permanent removal the caller confirmed.</param>
    /// <param name="cancellationToken">Stops before the next deletion; already deleted images remain deleted.</param>
    /// <returns>Per-file outcomes; successful deletions have no destination path.</returns>
    public Task<ScreenshotBatchResult> DeleteAsync(string instanceId, IReadOnlyList<ScreenshotFile> files, CancellationToken cancellationToken = default) =>
        RunBatchAsync(instanceId, files, file =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            File.Delete(file.Path);
            return Task.FromResult<string?>(null);
        }, cancellationToken);

    private Task<ScreenshotBatchResult> RunBatchAsync(string instanceId, IReadOnlyList<ScreenshotFile> files,
        Func<ScreenshotFile, Task<string?>> action, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(files);
        if (files.Count == 0) throw new ArgumentException("Select at least one screenshot.", nameof(files));
        var selected = files.ToArray();
        if (selected.Select(file => file.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != selected.Length)
            throw new ArgumentException("A screenshot can only be selected once.", nameof(files));
        return Task.Run(async () =>
        {
            var directory = GetDirectory(instanceId);
            var results = new List<ScreenshotFileResult>();
            try
            {
                using var lease = await ResourceLease.AcquireAsync(directory, ct).ConfigureAwait(false);
                foreach (var file in selected)
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        var current = ValidateSelection(directory, file);
                        results.Add(new(file.FileName, await action(current).ConfigureAwait(false)));
                    }
                    catch (Exception error) when (IsFileError(error))
                    {
                        var detail = LauncherOperationException.CreateResult(error).Error!;
                        results.Add(new(file.FileName, null, detail.Code, detail.Message));
                    }
                }

                return new ScreenshotBatchResult(results, false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                return new ScreenshotBatchResult(results, true);
            }
        });
    }

    private static ScreenshotFile ValidateSelection(string directory, ScreenshotFile selected)
    {
        var current = ReadFile(directory, selected.FileName);
        if (!current.Path.Equals(selected.Path, StringComparison.OrdinalIgnoreCase)
            || current.Bytes != selected.Bytes || current.ModifiedUtc != selected.ModifiedUtc)
        {
            throw new LauncherOperationException("SCREENSHOT_CHANGED", "The selected screenshot changed. Refresh and select it again.");
        }

        return current;
    }

    private static ScreenshotFile ReadFile(string directory, string name)
    {
        ValidateName(name);
        var path = Path.Combine(directory, name);
        var attributes = File.GetAttributes(path);
        if ((attributes & FileAttributes.Directory) != 0)
            throw new ArgumentException("Select an image file, not a directory.", nameof(name));
        var file = new FileInfo(path);
        return new(name, path, file.Length, file.LastWriteTimeUtc);
    }

    private static void ValidateName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name is "." or ".." || name.TrimEnd(' ', '.') != name
            || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name || !IsImage(name))
        {
            throw new ArgumentException("Select a single PNG, JPEG, WebP, BMP or GIF screenshot file name.", nameof(name));
        }
    }

    private static bool IsImage(string path) => Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".webp" or ".bmp" or ".gif";

    private static bool IsFileError(Exception error) =>
        error is IOException or UnauthorizedAccessException or ArgumentException or LauncherOperationException or COMException or PlatformNotSupportedException;
}