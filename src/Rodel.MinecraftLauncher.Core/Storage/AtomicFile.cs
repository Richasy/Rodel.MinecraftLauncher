// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Storage;

/// <summary>Publishes complete stream copies through a flushed same-directory temporary file; callers own validation, leases and recovery.</summary>
internal static class AtomicFile
{
    /// <summary>Copies the input's remaining bytes, flushes and closes the temporary output, then replaces the destination using move semantics.</summary>
    /// <param name="input">The borrowed source stream, read from its current position and never disposed here.</param>
    /// <param name="destination">The caller-validated destination whose parent directory is created when needed.</param>
    /// <param name="bufferSize">The output buffer size selected by the owning operation.</param>
    /// <param name="options">The output file options selected by the owning operation.</param>
    /// <param name="cancellationToken">Cancels copying and the final check before publication; an existing destination is retained before that commit.</param>
    /// <param name="overwrite">Whether publication may replace an existing destination; false also protects a destination created during copying.</param>
    /// <returns>A task completing after publication and cleanup of this attempt's temporary file.</returns>
    internal static async Task CopyAsync(Stream input, string destination, int bufferSize, FileOptions options, CancellationToken cancellationToken, bool overwrite = true)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            {
                var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, bufferSize, options);
                await using (output.ConfigureAwait(false))
                {
                    await input.CopyToAsync(output, cancellationToken).ConfigureAwait(false);
                    await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                    output.Flush(flushToDisk: true);
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporary, destination, overwrite);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }
}