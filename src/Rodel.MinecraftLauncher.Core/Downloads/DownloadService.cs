// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Downloads;

/// <summary>Reports measured work for one acquired file and its containing operation.</summary>
/// <param name="Path">The selected destination file.</param>
/// <param name="Bytes">The bytes processed in the current activity.</param>
/// <param name="TotalBytes">The known size of that activity, or null when unavailable.</param>
/// <param name="Reused">Whether verified local bytes replaced network transfer.</param>
/// <param name="SourceUri">The actual network source, when transferring bytes.</param>
public sealed record DownloadProgress(string Path, long Bytes, long? TotalBytes, bool Reused, string? SourceUri = null)
{
    /// <summary>Gets the actual acquisition activity when it differs from network transfer.</summary>
    public string? Stage { get; init; }
    /// <summary>Gets measured completion of the containing file batch, independently of the current file's bytes.</summary>
    public double? OverallPercent { get; init; }
}

/// <summary>Verified range recovery with ordinary full downloads as the compatibility fallback.</summary>
public sealed class DownloadService(LauncherPaths paths, HttpClient client, TimeSpan? readIdleTimeout = null)
{
    private readonly TimeSpan _readIdleTimeout = readIdleTimeout ?? NetworkSettings.IdleTimeout(paths);
    /// <summary>Downloads an asset with optional SHA1/SHA256/SHA512 verification, progress and resumable transfer handling.</summary>
    public async Task<bool> DownloadAsync(Uri uri, string destination, string? sha1 = null, long? size = null, IProgress<DownloadProgress>? progress = null, Func<Uri, IReadOnlyDictionary<string, string>>? headersForUri = null, string? sha512 = null, string? sha256 = null, CancellationToken cancellationToken = default)
    {
        using var cacheUse = await CacheActivity.EnterAsync(paths, cancellationToken).ConfigureAwait(false);
        var hasIntegrityHash = !string.IsNullOrWhiteSpace(sha1) || !string.IsNullOrWhiteSpace(sha512) || !string.IsNullOrWhiteSpace(sha256);
        var originalOnly = false;
        destination = Path.GetFullPath(destination);
        var recordOwnership = DownloadOwnership.Within(destination, paths.CacheDirectory) || DownloadOwnership.Within(destination, paths.InstancesDirectory);
        using var lease = await ResourceLease.AcquireAsync(resource: destination, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (File.Exists(destination) && await ExistingValidAsync(destination, sha1, sha512, sha256, size, cancellationToken).ConfigureAwait(false))
        {
            progress?.Report(new(destination, new FileInfo(destination).Length, size, true, uri.ToString()));
            return false;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        var partial = destination + ".rodel.partial";
        var record = partial + ".json";
        JsonObject State() => new()
        {
            ["url"] = uri.ToString(),
            ["destination"] = destination,
            ["sha1"] = sha1,
            ["sha512"] = sha512,
            ["sha256"] = sha256,
            ["size"] = size,
            ["strategy"] = "range-with-full-fallback",
            ["state"] = "downloading"
        };
        var saved = State();
        if (File.Exists(record) && File.Exists(partial))
        {
            try
            {
                var previous = JsonFiles.ReadObject(record);
                if (previous["url"]?.ToString() == uri.ToString() && previous["destination"]?.ToString() == destination && previous["sha1"]?.ToString() == sha1 && previous["sha512"]?.ToString() == sha512 && previous["sha256"]?.ToString() == sha256 && previous["size"]?.ToString() == size?.ToString())
                    saved = previous;
                else
                    File.Delete(partial);
            }
            catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidOperationException or InvalidDataException)
            {
                File.Delete(partial);
            }
        }
        else if (File.Exists(partial))
        {
            File.Delete(partial);
        }

        await JsonFiles.WriteAsync(record, saved, cancellationToken).ConfigureAwait(false);
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                var validator = saved["etag"] is JsonValue tagValue && tagValue.TryGetValue<string>(out var tagText) ? tagText : null;
                var modified = saved["lastModified"] is JsonValue dateValue && dateValue.TryGetValue<string>(out var dateText) ? dateText : null;
                if (!EntityTagHeaderValue.TryParse(validator, out var storedTag) || storedTag.IsWeak)
                    validator = null;
                if (!DateTimeOffset.TryParse(modified, out _))
                    modified = null;
                if (!hasIntegrityHash && saved["lastModifiedStrong"]?.ToString() != "true")
                    modified = null;
                var offset = File.Exists(partial) && (hasIntegrityHash || validator is not null || modified is not null) ? new FileInfo(partial).Length : 0;
                using var response = await DownloadRequests.SendAsync(client: client, uri: uri, headersForUri: headersForUri, ct: cancellationToken, configure: request =>
                {
                    request.Options.Set(DownloadRequests.OriginalSourceOnly, originalOnly);
                    request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
                    if (offset == 0)
                        return;
                    request.Headers.Range = new RangeHeaderValue(offset, null);
                    if (validator is not null)
                        request.Headers.IfRange = new RangeConditionHeaderValue(EntityTagHeaderValue.Parse(validator));
                    else if (DateTimeOffset.TryParse(modified, out var date))
                        request.Headers.IfRange = new RangeConditionHeaderValue(date);
                }).ConfigureAwait(false);
                if (response.StatusCode == HttpStatusCode.TooManyRequests || (int)response.StatusCode >= 500)
                {
                    if (attempt >= 3)
                        response.EnsureSuccessStatusCode();
                    var retry = response.Headers.RetryAfter?.Delta ?? (response.Headers.RetryAfter?.Date is { } date ? date - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(attempt + 1));
                    await Task.Delay(TimeSpan.FromSeconds(Math.Clamp(retry.TotalSeconds, 0.1, 30)), cancellationToken).ConfigureAwait(false);
                    continue;
                }

                var range = response.Content.Headers.ContentRange;
                if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && offset > 0)
                {
                    var expected = size ?? range?.Length;
                    var recovered = expected == offset && hasIntegrityHash
                        ? await VerifiedHashesAsync(partial, sha1, sha512, sha256, expected, cancellationToken, ownership: recordOwnership).ConfigureAwait(false) : null;
                    if (recovered is not null)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        File.Move(partial, destination, true);
                        File.Delete(record);
                        await DownloadOwnership.RecordAsync(paths, destination, cancellationToken, recovered).ConfigureAwait(false);
                        progress?.Report(new(destination, offset, expected, false, response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString()));
                        return true;
                    }

                    Reset();
                    if (attempt >= 3)
                        throw new InvalidDataException("Range recovery was rejected after bounded retries.");
                    continue;
                }

                response.EnsureSuccessStatusCode();
                if (response.StatusCode == HttpStatusCode.PartialContent && (range is null || range.Unit != "bytes" || range.From != offset || range.To is null || range.Length is null || range.To >= range.Length || (size is not null && size != range.Length) || (response.Content.Headers.ContentLength is { } segment && segment != range.To - range.From + 1) || response.Content.Headers.ContentEncoding.Any(x => !x.Equals("identity", StringComparison.OrdinalIgnoreCase)) || (validator is not null && response.Headers.ETag is { } newTag && newTag.ToString() != validator) || (modified is not null && DateTimeOffset.TryParse(modified, out var previousDate) && response.Content.Headers.LastModified is { } changedDate && previousDate != changedDate) || (offset > 0 && !hasIntegrityHash && (saved["representationUrl"]?.GetValue<string>() ?? uri.ToString()) != response.RequestMessage?.RequestUri?.ToString())))
                {
                    Reset();
                    throw new InvalidDataException("Server returned an inconsistent byte range; retrying a full download.");
                }

                // A server may ignore Range or If-Range and return the entire new representation.
                if (response.StatusCode != HttpStatusCode.PartialContent)
                    offset = 0;
                var total = size ?? (response.StatusCode == HttpStatusCode.PartialContent ? range!.Length : response.Content.Headers.ContentLength);
                {
                    var input = (await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false));
                    await using (input.ConfigureAwait(false))
                    {
                        var output = new FileStream(partial, offset > 0 ? FileMode.Append : FileMode.Create, FileAccess.Write, FileShare.None, 65536, FileOptions.Asynchronous | FileOptions.WriteThrough);
                        await using (output.ConfigureAwait(false))
                        {
                            saved = State();
                            saved["etag"] = response.Headers.ETag is { IsWeak: false } tag ? tag.ToString() : null;
                            saved["lastModified"] = response.Content.Headers.LastModified?.ToString("R");
                            saved["lastModifiedStrong"] = response.Content.Headers.LastModified is { } lastModified && response.Headers.Date is { } responseDate && responseDate - lastModified >= TimeSpan.FromSeconds(60);
                            saved["totalBytes"] = total;
                            saved["representationUrl"] = response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString();
                            await JsonFiles.WriteAsync(record, saved, cancellationToken).ConfigureAwait(false);
                            using var readDeadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                            var buffer = new byte[65536];
                            var received = offset;
                            int count;
                            while (true)
                            {
                                // ResponseHeadersRead ends HttpClient.Timeout at the headers. Each body
                                // read gets its own idle deadline; continuing slow downloads have no total limit.
                                readDeadline.CancelAfter(_readIdleTimeout);
                                try
                                {
                                    count = await input.ReadAsync(buffer, readDeadline.Token).ConfigureAwait(false);
                                }
                                catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested && readDeadline.IsCancellationRequested)
                                {
                                    throw new HttpRequestException($"Download body made no progress for {_readIdleTimeout}: {destination}", exception);
                                }
                                finally
                                {
                                    readDeadline.CancelAfter(Timeout.InfiniteTimeSpan);
                                }

                                if (count == 0)
                                    break;
                                await output.WriteAsync(buffer.AsMemory(0, count), cancellationToken).ConfigureAwait(false);
                                received += count;
                                progress?.Report(new(destination, received, total, false, response.RequestMessage?.RequestUri?.ToString() ?? uri.ToString()));
                            }

                            await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                            output.Flush(true);
                            if (total is { } length && received != length)
                                throw new IOException($"Download length mismatch: {destination}");
                        }
                    }
                }

                var verified = await VerifiedHashesAsync(partial, sha1, sha512, sha256, size, cancellationToken, ownership: recordOwnership).ConfigureAwait(false);
                if (verified is null)
                {
                    Reset();
                    throw new InvalidDataException($"Download checksum mismatch: {destination}");
                }

                cancellationToken.ThrowIfCancellationRequested();
                File.Move(partial, destination, true);
                File.Delete(record);
                await DownloadOwnership.RecordAsync(paths, destination, cancellationToken, verified).ConfigureAwait(false);
                return true;
            }
            catch (Exception exception) when (!IsPermanentIoError(exception) && (exception is not HttpRequestException { StatusCode: { } status } || status == HttpStatusCode.TooManyRequests || (int)status >= 500) && (exception is IOException or HttpRequestException or InvalidDataException || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested)) && attempt < 3)
            {
                // The next bounded attempt uses the original after transport or integrity
                // failures as well, so a stalled mirror cannot monopolize every retry.
                originalOnly = true;
                await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                throw new HttpRequestException($"Download request timed out after bounded retries: {destination}", exception);
            }
        }

        void Reset()
        {
            if (File.Exists(partial))
                File.Delete(partial);
            saved = State();
        }
    }

    private static async Task<bool> ExistingValidAsync(string path, string? sha1, string? sha512, string? sha256, long? size, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                return await VerifiedHashesAsync(path, sha1, sha512, sha256, size, ct).ConfigureAwait(false) is not null;
            }
            catch (IOException ex) when (attempt < 10 && (unchecked((uint)ex.HResult) & 0xffff0000u) == 0x80070000u && (ex.HResult & 0xffff) is 32 or 33)
            {
                await Task.Delay(100, ct).ConfigureAwait(false);
            }
        }
    }

    private static async Task<FileDigests?> VerifiedHashesAsync(string path, string? sha1, string? sha512, string? sha256, long? size, CancellationToken ct, bool ownership = false)
    {
        if (!File.Exists(path) || (size is { } expected && new FileInfo(path).Length != expected))
            return null;
        if (!ownership && string.IsNullOrWhiteSpace(sha1) && string.IsNullOrWhiteSpace(sha256) && string.IsNullOrWhiteSpace(sha512))
            return new(new FileInfo(path).Length, null, null, null);
        var hashes = await FileIntegrity.ReadAsync(path, !string.IsNullOrWhiteSpace(sha1), ownership || !string.IsNullOrWhiteSpace(sha256), !string.IsNullOrWhiteSpace(sha512), ct).ConfigureAwait(false);
        return (string.IsNullOrWhiteSpace(sha1) || string.Equals(hashes.Sha1, sha1, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(sha256) || string.Equals(hashes.Sha256, sha256, StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(sha512) || string.Equals(hashes.Sha512, sha512, StringComparison.OrdinalIgnoreCase))
            && (size is null || hashes.Size == size) ? hashes : null;
    }

    private static bool IsPermanentIoError(Exception exception)
    {
        if (exception is PathTooLongException or FileNotFoundException or DirectoryNotFoundException)
            return true;
        if (exception is not IOException io)
            return false;
        // Only classify Win32 HRESULTs: generic/transport IO errors retain bounded retries.
        if ((unchecked((uint)io.HResult) & 0xffff0000u) != 0x80070000u)
            return false;
        return (io.HResult & 0xffff) is 2 or 3 or 15 or 19 or 39 or 87 or 112 or 123 or 161 or 206 or 267;
    }

    /// <summary>Checks a local file against the supplied length and SHA-1 without changing or publishing its bytes.</summary>
    public static async Task<bool> ValidAsync(string path, string? sha1, long? size, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(path))
            return false;
        if (size is { } expected && new FileInfo(path).Length != expected)
            return false;
        if (string.IsNullOrWhiteSpace(sha1))
            return true;
        var actual = (await FileIntegrity.ReadAsync(path, true, false, false, cancellationToken).ConfigureAwait(false)).Sha1!;
        return actual.Equals(sha1, StringComparison.OrdinalIgnoreCase);
    }
}