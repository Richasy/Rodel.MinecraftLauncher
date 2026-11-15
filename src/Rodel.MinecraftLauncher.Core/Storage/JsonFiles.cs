// Copyright (c) Richasy. All rights reserved.

using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Storage;

public static class JsonFiles
{
    /// <summary>Reads an object snapshot, briefly retrying transient Windows name or sharing failures during atomic replacement.</summary>
    /// <param name="path">The JSON record to read; permanently missing records and malformed content still fail.</param>
    /// <returns>The parsed object without converting absent or invalid data into an empty record.</returns>
    public static JsonObject ReadObject(string path) => ReadSnapshot(path, stream =>
        JsonNode.Parse(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }) as JsonObject
            ?? throw new InvalidDataException($"Expected a JSON object: {path}"));

    internal static T ReadSnapshot<T>(string path, Func<Stream, T> read)
    {
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return read(stream);
            }
            // Concurrent atomic replacement can briefly hide the name or deny a shared-delete reader.
            // Bound retries for missing names and sharing locks; absent or malformed records still fail.
            catch (IOException exception) when (attempt < 5 && (exception.HResult & 0xffff) is 2 or 32 or 33)
            {
                Thread.Sleep(20);
            }
        }
    }

    /// <summary>Publishes readable JSON through a durable temporary file, preserving the previous record on cancellation or replacement failure.</summary>
    /// <param name="path">The record to create or atomically replace.</param>
    /// <param name="value">The complete object or array snapshot.</param>
    /// <param name="cancellationToken">Cancels before the atomic replacement is committed.</param>
    public static Task WriteAsync(string path, JsonNode value, CancellationToken cancellationToken = default) =>
        WriteSnapshotAsync(path, async (stream, ct) =>
        {
            using var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true });
            value.WriteTo(writer);
            await writer.FlushAsync(ct).ConfigureAwait(false);
        }, cancellationToken);

    internal static Task WriteBytesAsync(string path, byte[] value, CancellationToken cancellationToken) =>
        WriteSnapshotAsync(path, (stream, ct) => stream.WriteAsync(value, ct).AsTask(), cancellationToken);

    private static async Task WriteSnapshotAsync(string path, Func<Stream, CancellationToken, Task> write, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            {
                var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
                await using (stream.ConfigureAwait(false))
                {
                    await write(stream, cancellationToken).ConfigureAwait(false);
                    stream.Flush(flushToDisk: true);
                }
            }

            for (var attempt = 0; ; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    // ReplaceFile preserves an open reader's old snapshot on Windows. MoveFileEx
                    // can deny replacement even when that reader granted FileShare.Delete.
                    if (File.Exists(path))
                        File.Replace(temporary, path, destinationBackupFileName: null);
                    else
                        File.Move(temporary, path);
                    break;
                }
                catch (IOException exception) when (attempt < 10 && (exception.HResult & 0xffff) is 32 or 33)
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                // ReplaceFile error 1175 leaves both original names intact. A concurrent
                // observer can cause this transient deletion denial just like a sharing lock.
                // 1176/1177 have different recovery semantics and must not be retried here.
                catch (IOException exception) when (attempt < 10 && (exception.HResult & 0xffff) == 1175 && CanRetryReplacement(path))
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
                catch (UnauthorizedAccessException) when (attempt < 10 && CanRetryReplacement(path))
                {
                    await Task.Delay(100, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    private static bool CanRetryReplacement(string path)
    {
        // Windows may temporarily deny replacing a file while an observer closes its handle.
        // A known read-only destination is a persistent error, so do not retry it.
        try
        {
            return File.Exists(path) && (File.GetAttributes(path) & FileAttributes.ReadOnly) == 0;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }
}