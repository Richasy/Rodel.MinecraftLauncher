// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

/// <summary>Composes the installed Forge MCP configuration with official mappings; no member is guessed by type.</summary>
internal static class BridgeSrgMappings
{
    internal static async Task<byte[]?> ReadOfficialClientMappingsAsync(string minecraft, JsonObject metadata, string sha1, long? size, CancellationToken ct)
    {
        var game = metadata["_minecraftVersion"]?.GetValue<string>();
        if (!System.Version.TryParse(game, out _)) return null;
        var root = SharedRuntimeLayout.Resolve(minecraft, "libraries/net/minecraft/client");
        if (!Directory.Exists(root)) return null;
        // Forge's DOWNLOAD_MOJMAP processor leaves the original official file here. Its bytes, not its filename,
        // establish reuse: merged/SRG files or a damaged cache must never become official bindings.
        foreach (var directory in Directory.EnumerateDirectories(root).Where(directory => Path.GetFileName(directory) == game
            || Path.GetFileName(directory).StartsWith(game + "-", StringComparison.Ordinal)).Take(16))
        {
            var file = Path.Combine(directory, "client-" + Path.GetFileName(directory) + "-mappings.txt");
            if (!File.Exists(file)) continue;
            var length = new FileInfo(file).Length;
            if (length > 32 * 1024 * 1024 || (size is not null && length != size)) continue;
            byte[] bytes;
            try { bytes = await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { continue; }

            if (Convert.ToHexStringLower(SHA1.HashData(bytes)) == sha1) return bytes;
        }

        return null;
    }

    internal static async Task<string> ReadAsync(string minecraft, JsonObject metadata, CancellationToken ct)
    {
        var game = metadata["_minecraftVersion"]?.GetValue<string>() ?? throw new InvalidDataException("A base game version is required for Forge mappings.");
        if (!System.Version.TryParse(game, out _)) throw new InvalidDataException("Invalid Forge mapping game version.");
        var coordinate = metadata["_rodelMcpConfig"]?.GetValue<string>();
        string file;
        if (coordinate is not null)
        {
            if (!coordinate.StartsWith("de.oceanlabs.mcp:mcp_config:", StringComparison.Ordinal)) throw new InvalidDataException("Invalid installed MCP configuration coordinate.");
            var root = SharedRuntimeLayout.Resolve(minecraft, "libraries");
            file = Path.GetFullPath(Path.Combine(root, MetadataRules.MavenPath(coordinate)));
            if (!file.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("The MCP configuration escaped the runtime libraries.");
        }
        else
        {
            var root = SharedRuntimeLayout.Resolve(minecraft, "libraries/de/oceanlabs/mcp/mcp_config");
            var candidates = Directory.Exists(root) ? Directory.EnumerateDirectories(root).Where(directory => Path.GetFileName(directory) == game
                || Path.GetFileName(directory).StartsWith(game + "-", StringComparison.Ordinal)).SelectMany(directory => Directory.EnumerateFiles(directory, "mcp_config-*.zip")).ToArray() : [];
            if (candidates.Length != 1) throw new InvalidDataException("The installed Forge runtime does not identify one exact MCP configuration.");
            file = candidates[0];
        }

        using var archive = ZipFile.OpenRead(file);
        var entry = archive.GetEntry("config/joined.tsrg") ?? throw new InvalidDataException("The installed MCP configuration has no SRG mappings.");
        if (entry.Length > 32 * 1024 * 1024) throw new InvalidDataException("The MCP mapping exceeds the bridge budget.");
        using var reader = new StreamReader(entry.Open());
        return await reader.ReadToEndAsync(ct).ConfigureAwait(false);
    }

    internal static Dictionary<(string Owner, string Name, string Descriptor), string> Parse(string? mapping)
    {
        var result = new Dictionary<(string Owner, string Name, string Descriptor), string>();
        string? owner = null;
        foreach (var line in (mapping ?? string.Empty).Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line) || line.StartsWith("tsrg", StringComparison.Ordinal) || line.StartsWith("\t\t", StringComparison.Ordinal)) continue;
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!char.IsWhiteSpace(line[0])) { owner = parts[0].Replace('/', '.'); continue; }

            if (owner is null || parts.Length < 2) continue;
            if (parts.Length >= 3 && parts[1].StartsWith('(')) result[(owner, parts[0], parts[1])] = parts[2];
            else result[(owner, parts[0], string.Empty)] = parts[1];
        }

        return result;
    }
}