// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

public sealed partial class ContentService
{
    public static bool IsContentFileName(string kind, string fileName)
    {
        if (fileName.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase)) fileName = fileName[..^9];
        var extension = Path.GetExtension(fileName).ToLowerInvariant();
        return kind.ToLowerInvariant() switch
        {
            "mods" => extension is ".jar" or ".zip" or ".litemod",
            "resourcepacks" or "shaderpacks" or "datapacks" => extension == ".zip",
            _ => throw new ArgumentException($"Unknown content kind: {kind}", nameof(kind)),
        };
    }

    /// <summary>Inspects one explicitly selected package without copying or modifying it.</summary>
    public static async Task<ContentEntry> InspectSourceAsync(string kind, string source, CancellationToken ct = default)
    {
        kind = kind.ToLowerInvariant();
        source = Path.GetFullPath(source);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Content source is missing.", source);
        var entry = await DescribeAsync(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source)) ?? Path.GetPathRoot(source)!, kind, source, ct, hashContent: false).ConfigureAwait(false);
        if (!entry.IsContent
            || (!entry.Directory && entry.ArchiveReadable != true)
            || (kind != "mods" && !entry.FormatRecognized))
        {
            throw new LauncherOperationException("CONTENT_IMPORT_UNRECOGNIZED",
                $"The selected entry is not a recognizable {kind} package. Configuration text, backups and ordinary folders are not installed as packages; the source was retained.",
                new JsonObject { ["kind"] = kind, ["source"] = source, ["inspection"] = entry.ToRecord() });
        }

        return entry;
    }

    /// <summary>Finds package types from archive/directory structure without using the active management tab.</summary>
    public static async Task<ContentSourceDetection> DetectSourceAsync(string source, CancellationToken ct = default)
    {
        source = Path.GetFullPath(source);
        if (!File.Exists(source) && !Directory.Exists(source)) throw new FileNotFoundException("Content source is missing.", source);
        var candidates = new List<ContentEntry>();
        ContentEntry? mod = null;
        foreach (var kind in new[] { "mods", "resourcepacks", "shaderpacks" })
        {
            var entry = await DescribeAsync(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(source)) ?? Path.GetPathRoot(source)!,
                kind, source, ct, hashContent: false).ConfigureAwait(false);
            if (!entry.IsContent) continue;
            if (kind == "mods") mod = entry;
            if (entry.FormatRecognized) candidates.Add(entry);
        }

        if (candidates.Count == 0 && mod?.ArchiveReadable == true
            && Path.GetExtension(source.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase) ? source[..^9] : source).Equals(".jar", StringComparison.OrdinalIgnoreCase))
        {
            candidates.Add(mod);
        }

        return new ContentSourceDetection { Source = source, Candidates = candidates };
    }

    internal static Task<string> SourceFingerprintAsync(string source, CancellationToken ct) => EntryDigestAsync(source, ct);

    private static bool IsContentDirectory(string kind, string path) => kind.ToLowerInvariant() switch
    {
        "mods" => false,
        "resourcepacks" or "datapacks" => File.Exists(Path.Combine(path, "pack.mcmeta")),
        "shaderpacks" => Directory.Exists(Path.Combine(path, "shaders")),
        _ => throw new ArgumentException($"Unknown content kind: {kind}", nameof(kind)),
    };

    private static bool HasArchiveLayout(string kind, ZipArchive archive) => kind.ToLowerInvariant() switch
    {
        "mods" => archive.GetEntry("fabric.mod.json") is not null || archive.GetEntry("quilt.mod.json") is not null
            || archive.GetEntry("META-INF/mods.toml") is not null || archive.GetEntry("META-INF/neoforge.mods.toml") is not null
            || archive.GetEntry("mcmod.info") is not null || archive.GetEntry("litemod.json") is not null,
        "resourcepacks" or "datapacks" => archive.GetEntry("pack.mcmeta") is not null,
        "shaderpacks" => archive.Entries.Any(entry =>
        {
            var name = entry.FullName.Replace('\\', '/');
            var first = name.IndexOf('/');
            return name.StartsWith("shaders/", StringComparison.Ordinal)
                || (first > 0 && name[(first + 1)..].StartsWith("shaders/", StringComparison.Ordinal));
        }),
        _ => throw new ArgumentException($"Unknown content kind: {kind}", nameof(kind)),
    };
}