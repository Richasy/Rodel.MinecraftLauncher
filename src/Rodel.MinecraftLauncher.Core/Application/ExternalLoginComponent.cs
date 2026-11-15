// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A launcher-owned authlib-injector artifact whose bytes and Java agent manifest have been checked.</summary>
/// <param name="Path">Absolute path to the owned JAR.</param>
/// <param name="Sha256">Expected SHA-256 of the owned bytes.</param>
public sealed record ExternalLoginComponent(string Path, string Sha256);

public sealed partial class IdentityService
{
    private string ComponentDirectory => Path.Combine(paths.Root, "dependencies", "authlib-injector");
    private string ComponentRecord => Path.Combine(ComponentDirectory, "component.json");

    /// <summary>Reads and verifies the existing login component without downloading or executing Java.</summary>
    public ExternalLoginComponent? GetExternalLoginComponent()
    {
        try
        {
            if (!File.Exists(ComponentRecord)) return null;
            var record = JsonFiles.ReadObject(ComponentRecord);
            var hash = ExternalAuthTransport.Text(record, "sha256");
            if (hash?.Length != 64 || !hash.All(Uri.IsHexDigit)) return null;
            var file = Path.Combine(ComponentDirectory, hash.ToLowerInvariant() + ".jar");
            return File.Exists(file) && string.Equals(CheckJar(file), hash, StringComparison.OrdinalIgnoreCase) ? new(file, hash.ToLowerInvariant()) : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or System.Text.Json.JsonException or InvalidOperationException) { return null; }
    }

    /// <summary>Reuses a verified component, downloads and checks an official artifact, or imports the explicitly selected local JAR into owned storage.</summary>
    public async Task<ExternalLoginComponent> PrepareExternalLoginComponentAsync(string? localFile = null, CancellationToken ct = default)
    {
        using var cache = await CacheActivity.EnterAsync(paths, ct).ConfigureAwait(false);
        using var lease = await ResourceLease.AcquireAsync(ComponentRecord, ct).ConfigureAwait(false);
        if (localFile is null && GetExternalLoginComponent() is { } existing) return existing;
        Directory.CreateDirectory(ComponentDirectory);
        var temporary = Path.Combine(ComponentDirectory, Guid.NewGuid().ToString("N") + ".partial");
        try
        {
            string? expected = null;
            if (localFile is not null)
            {
                using var source = File.OpenRead(Path.GetFullPath(localFile));
                using var output = File.Create(temporary);
                await source.CopyToAsync(output, ct).ConfigureAwait(false);
            }
            else
            {
                var client = authenticationHttp ?? NetworkSettings.Client(paths, TimeSpan.FromSeconds(60));
                using var metadataResponse = await DownloadRequests.GetBufferedAsync(client, new Uri("https://authlib-injector.yushi.moe/artifact/latest.json"), NetworkSettings.MetadataTimeout(paths), ct).ConfigureAwait(false);
                var artifact = await ExternalAuthTransport.ReadAsync(metadataResponse, ct: ct).ConfigureAwait(false) as JsonObject ?? throw ExternalAuthTransport.InvalidResponse();
                expected = ExternalAuthTransport.Text(artifact?["checksums"], "sha256");
                var url = ExternalAuthTransport.Address(ExternalAuthTransport.Text(artifact, "download_url") ?? throw ExternalAuthTransport.InvalidResponse());
                if (expected?.Length != 64 || !expected.All(Uri.IsHexDigit) || url.Scheme != "https") throw ExternalAuthTransport.InvalidResponse();
                const string mirror = "https://bmclapi2.bangbang93.com/mirrors/authlib-injector/";
                if (url.AbsoluteUri.StartsWith(mirror, StringComparison.Ordinal))
                    url = new Uri("https://authlib-injector.yushi.moe/" + url.AbsoluteUri[mirror.Length..]);
                await new DownloadService(paths, client).DownloadAsync(url, temporary, sha256: expected, cancellationToken: ct).ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            var hash = CheckJar(temporary);
            if (expected is not null && !hash.Equals(expected, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The login component checksum does not match its official artifact metadata.");
            var destination = Path.Combine(ComponentDirectory, hash + ".jar");
            File.Move(temporary, destination, true);
            await JsonFiles.WriteAsync(ComponentRecord, new JsonObject { ["sha256"] = hash }, ct).ConfigureAwait(false);
            return new(destination, hash);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) when (ex is IOException or InvalidDataException or HttpRequestException or LauncherOperationException or System.Text.Json.JsonException)
        {
            throw new LauncherOperationException("AUTH_COMPONENT_FAILED", "The external login component could not be prepared. Retry or select an existing authlib-injector JAR.", new JsonObject { ["original"] = ex.Message });
        }
        finally
        {
            foreach (var file in new[] { temporary, temporary + ".rodel.partial", temporary + ".rodel.partial.json" })
                if (File.Exists(file)) File.Delete(file);
        }
    }

    private static string CheckJar(string file)
    {
        using var input = File.OpenRead(file);
        var hash = Convert.ToHexStringLower(SHA256.HashData(input));
        input.Position = 0;
        using var zip = new ZipArchive(input, ZipArchiveMode.Read);
        var manifest = zip.GetEntry("META-INF/MANIFEST.MF") ?? throw new InvalidDataException("The selected file has no Java manifest.");
        if (manifest.Length > 64 * 1024) throw new InvalidDataException("The Java manifest is too large.");
        using var reader = new StreamReader(manifest.Open(), Encoding.UTF8);
        var text = reader.ReadToEnd().Replace("\r\n ", string.Empty, StringComparison.Ordinal).Replace("\n ", string.Empty, StringComparison.Ordinal);
        if (!text.Split('\n').Any(line => line.TrimEnd('\r').Equals("Premain-Class: moe.yushi.authlibinjector.Premain", StringComparison.Ordinal))
            || zip.GetEntry("moe/yushi/authlibinjector/Premain.class") is null)
        {
            throw new InvalidDataException("The selected JAR is not an authlib-injector Java agent.");
        }

        return hash;
    }

    /// <summary>Reports whether this account has all protected session, metadata and component bytes needed for an explicit cached attempt.</summary>
    public bool CanUseCachedExternalSession(string id)
    {
        try { return ExternalIdentity(id).Status == "ready" && ReadExternalCredentials(ExternalIdentity(id))["metadata"] is JsonObject && GetExternalLoginComponent() is not null; }
        catch (LauncherOperationException) { return false; }
    }
}