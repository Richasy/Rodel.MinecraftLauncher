// Copyright (c) Richasy. All rights reserved.

using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.App.Features.Updates;

/// <summary>Reads the stable release channel and downloads only an exact, manifest-verified installer.</summary>
internal sealed partial class GitHubUpdateClient(HttpClient http)
{
    internal const string Repository = "https://github.com/Richasy/Rodel.MinecraftLauncher";
    internal static readonly Uri Latest = new("https://api.github.com/repos/Richasy/Rodel.MinecraftLauncher/releases/latest");
    private const long MaximumInstallerSize = 2L * 1024 * 1024 * 1024;

    internal async Task<AvailableUpdate?> CheckAsync(Version current, string skipped, string architecture, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(20));
            var release = await ReadJsonAsync(Latest, ReleaseJsonContext.Default.GitHubRelease, timeout.Token).ConfigureAwait(false);
            if (release.Draft || release.Prerelease || !release.TagName.StartsWith('v')
                || !TryVersion(release.TagName[1..], out var version) || version <= current || version.ToString(4) == skipped)
            {
                return null;
            }

            var prefix = $"{Repository}/releases/download/{release.TagName}/";
            var notesAsset = release.Assets.Single(a => a.Name == "release-note.json");
            RequireAsset(notesAsset, prefix, 1024 * 1024);
            var manifest = await ReadJsonAsync(new(notesAsset.DownloadUrl), ReleaseJsonContext.Default.ReleaseManifest, timeout.Token).ConfigureAwait(false);
            if (manifest.SchemaVersion != 1 || manifest.Version != version.ToString(4)
                || !CommitPattern().IsMatch(manifest.SourceCommit)
                || !manifest.Notes.TryGetValue("en", out var english) || string.IsNullOrWhiteSpace(english)
                || !manifest.Installers.TryGetValue(architecture, out var installer)
                || installer.FileName != $"RMCL-{manifest.Version}-win-{architecture}-setup.exe"
                || !HashPattern().IsMatch(installer.Sha256) || installer.Size is <= 0 or > MaximumInstallerSize)
            {
                return null;
            }

            var asset = release.Assets.Single(a => a.Name == installer.FileName);
            RequireAsset(asset, prefix, MaximumInstallerSize);
            return asset.Size != installer.Size ? null : new(manifest, installer, new(asset.DownloadUrl));
        }
        catch (Exception)
        {
            // Startup discovery is optional; offline, rate-limited and malformed releases must never interrupt launch.
            return null;
        }
    }

    internal async Task<string> DownloadAsync(AvailableUpdate update, string directory, IProgress<long> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, update.Installer.FileName);
        var temporary = target + "." + Guid.NewGuid().ToString("N") + ".part";
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromMinutes(30));
            using var request = Request(update.DownloadUri);
            using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();
            if (response.Content.Headers.ContentLength is { } length && length != update.Installer.Size)
                throw new InvalidDataException("The installer size does not match the release manifest.");
            await using var input = await response.Content.ReadAsStreamAsync(timeout.Token).ConfigureAwait(false);
            using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            await using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, FileOptions.Asynchronous))
            {
                var buffer = new byte[81920];
                long received = 0;
                int count;
                while ((count = await input.ReadAsync(buffer, timeout.Token).ConfigureAwait(false)) > 0)
                {
                    received += count;
                    if (received > update.Installer.Size)
                        throw new InvalidDataException("The installer exceeded the release size.");
                    hash.AppendData(buffer, 0, count);
                    await output.WriteAsync(buffer.AsMemory(0, count), timeout.Token).ConfigureAwait(false);
                    progress.Report(received);
                }

                if (received != update.Installer.Size || !Convert.ToHexStringLower(hash.GetHashAndReset()).Equals(update.Installer.Sha256, StringComparison.Ordinal))
                    throw new InvalidDataException("The installer checksum does not match the release manifest.");
                await output.FlushAsync(timeout.Token).ConfigureAwait(false);
            }

            timeout.Token.ThrowIfCancellationRequested();
            File.Move(temporary, target, true);
            return target;
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

    internal static bool TryVersion(string text, out Version version)
    {
        version = new();
        return VersionPattern().IsMatch(text) && Version.TryParse(text, out version!)
            && version.Major <= 65535 && version.Minor <= 65535 && version.Build <= 65535 && version.Revision == 0;
    }

    private async Task<T> ReadJsonAsync<T>(Uri uri, JsonTypeInfo<T> type, CancellationToken cancellationToken)
    {
        using var request = Request(uri);
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        await response.Content.LoadIntoBufferAsync(1024 * 1024, cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize(await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false), type)
            ?? throw new InvalidDataException("The release document is empty.");
    }

    private static HttpRequestMessage Request(Uri uri)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("RMCL-Update/1.0");
        request.Headers.Accept.ParseAdd("application/json");
        return request;
    }

    private static void RequireAsset(GitHubAsset asset, string prefix, long maximum)
    {
        if (asset.Size <= 0 || asset.Size > maximum || asset.DownloadUrl != prefix + asset.Name)
            throw new InvalidDataException("The asset is not an exact release attachment.");
    }

    [GeneratedRegex(@"^(0|[1-9]\d*)\.(0|[1-9]\d*)\.(0|[1-9]\d*)\.0$", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPattern();
    [GeneratedRegex("^[a-f0-9]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex HashPattern();
    [GeneratedRegex("^[a-f0-9]{40}$", RegexOptions.CultureInvariant)]
    private static partial Regex CommitPattern();
}