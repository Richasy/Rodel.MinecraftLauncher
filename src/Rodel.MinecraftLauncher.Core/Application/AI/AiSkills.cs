// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>An immutable per-turn instruction snapshot. Skill scripts are never executed.</summary>
internal sealed class AiSkills
{
    private static readonly string[] Names = ["launcher-basics", "instance-diagnosis", "settings-explained", "task-observation", "skin-editing"];
    private readonly Dictionary<string, AiSkillReport> _skills = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Root, string SkillFile)> _roots = new(StringComparer.Ordinal);
    private readonly JsonArray _warnings = [];
    private long _configurationRevision;

    /// <summary>Loads product instructions and explicitly enabled user directories into one bounded snapshot.</summary>
    public static async Task<AiSkills> LoadAsync(LauncherPaths paths, CancellationToken ct)
    {
        var catalog = new AiSkills();
        foreach (var item in await BuiltinsAsync(ct).ConfigureAwait(false))
            catalog._skills.Add(item.Key, item.Value);
        var extensions = await new AiExtensionStore(paths).GetAsync(ct).ConfigureAwait(false);
        catalog._configurationRevision = extensions.Revision!.Value;
        foreach (var directory in extensions.SkillDirectories!)
        {
            if (directory?["enabled"]?.GetValue<bool>() != true) continue;
            catalog.LoadDirectory(directory.AsObject(), ct);
        }

        return catalog;
    }

    internal static async Task<IReadOnlyDictionary<string, AiSkillReport>> BuiltinsAsync(CancellationToken ct)
    {
        var skills = new Dictionary<string, AiSkillReport>(StringComparer.Ordinal);
        foreach (var name in Names)
        {
            using var stream = typeof(AiSkills).Assembly.GetManifestResourceStream("Rodel.MinecraftLauncher.Core.Application.AI.Skills." + name.Replace('-', '_') + ".SKILL.md")
                ?? throw new LauncherOperationException("AI_SKILL_PACKAGE_MISSING", "A packaged skill resource is missing. Repair this installation.");
            using var reader = new StreamReader(stream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync(ct).ConfigureAwait(false);
            skills.Add("builtin:" + name, Describe("builtin:" + name, name, body, "product"));
        }

        return skills;
    }

    private void LoadDirectory(JsonObject directory, CancellationToken ct)
    {
        var registrationId = directory["id"]!.GetValue<string>();
        var root = directory["path"]!.GetValue<string>();
        try
        {
            var files = new List<string>();
            if (File.Exists(Path.Combine(root, "SKILL.md"))) files.Add("SKILL.md");
            // Only explicitly registered roots and their immediate skill directories are indexed.
            var visited = 0;
            foreach (var child in Directory.EnumerateDirectories(root))
            {
                if (++visited > 100 || files.Count >= 100) throw new LauncherOperationException("AI_SKILL_INDEX_BUDGET", "One configured directory exceeded its 100-directory/skill indexing budget. Other configured sources remain available.");
                if (File.Exists(Path.Combine(child, "SKILL.md"))) files.Add(Path.Combine(Path.GetFileName(child), "SKILL.md"));
            }

            foreach (var relative in files)
            {
                ct.ThrowIfCancellationRequested();
                var id = "user:" + registrationId + ":" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16];
                try
                {
                    var body = AiScopedFiles.ReadText(root, relative);
                    var item = Describe(id, Path.GetFileName(Path.GetDirectoryName(Path.Combine(root, relative)))!, body, "user");
                    _skills.Add(id, item);
                    _roots.Add(id, (root, relative));
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException or ArgumentException or System.Text.Json.JsonException)
                {
                    _warnings.Add((JsonNode)new JsonObject { ["id"] = id, ["code"] = "AI_SKILL_UNREADABLE", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException)
        {
            _warnings.Add((JsonNode)new JsonObject { ["directoryId"] = registrationId, ["code"] = "AI_SKILL_DIRECTORY_UNREADABLE", ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
        }
    }

    /// <summary>Lists metadata from this immutable instruction snapshot without loading bodies into the catalog.</summary>
    public AiSkillCatalog List() => new()
    {
        Items = [.. _skills.Values.Select(item => item with { Body = null, Metadata = new(item.Metadata) })],
        Warnings = _warnings.DeepClone().AsArray(),
        ConfigurationRevision = _configurationRevision,
        UserDirectoriesImplemented = false
    };
    /// <summary>Reads the selected instruction snapshot without executing scripts.</summary>
    public AiSkillReport Read(string id)
    {
        if (Names.Contains(id, StringComparer.Ordinal)) id = "builtin:" + id;
        return _skills.TryGetValue(id, out var item) ? item with { Metadata = new(item.Metadata) }
            : throw new LauncherOperationException("AI_SKILL_NOT_FOUND", "No enabled skill has this source-qualified identifier.");
    }

    /// <summary>Reads a scoped reference only while its parent skill still matches this turn's snapshot.</summary>
    public AiSkillReference ReadReference(string id, string relative)
    {
        if (!_roots.TryGetValue(id, out var root))
            throw new LauncherOperationException("AI_SKILL_REFERENCE_UNAVAILABLE", "This skill does not expose a local reference directory.");
        var latest = AiScopedFiles.ReadText(root.Root, root.SkillFile);
        if (Hash(latest) != _skills[id].Version)
            throw new LauncherOperationException("AI_SKILL_CHANGED", "This skill changed after the turn's snapshot. Its original instructions remain available; start a new turn to load the new version.");
        var skillRoot = Path.GetDirectoryName(Path.Combine(root.Root, root.SkillFile))!;
        var body = AiScopedFiles.ReadText(skillRoot, relative, 64000);
        return new() { SkillId = id, Path = relative, Version = Hash(body), Body = body };
    }

    private static AiSkillReport Describe(string id, string fallbackName, string body, string source)
    {
        if (body.Length > 128000) throw new LauncherOperationException("AI_SKILL_TOO_LARGE", "A skill exceeds its instruction budget.");
        var lines = body.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
        if (lines.Length < 3 || lines[0] != "---")
            throw new LauncherOperationException("AI_SKILL_METADATA_INVALID", "SKILL.md requires YAML frontmatter with name and description.");
        var end = Array.IndexOf(lines, "---", 1);
        if (end < 0) throw new LauncherOperationException("AI_SKILL_METADATA_INVALID", "Skill frontmatter has no closing delimiter.");
        var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 1; index < end; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line) || line.TrimStart().StartsWith('#')) continue;
            var colon = line.IndexOf(':');
            if (colon <= 0 || char.IsWhiteSpace(line[0])) continue;
            var key = line[..colon].Trim();
            var value = line[(colon + 1)..].Trim();
            if (metadata.ContainsKey(key))
                throw new LauncherOperationException("AI_SKILL_METADATA_INVALID", "Skill frontmatter contains a duplicate metadata key.");
            if ((value.StartsWith('"') && (value.Length < 2 || !value.EndsWith('"')))
                || (value.StartsWith('\'') && (value.Length < 2 || !value.EndsWith('\''))))
            {
                throw new LauncherOperationException("AI_SKILL_METADATA_INVALID", "Skill frontmatter contains an unterminated quoted value.");
            }

            if (value is ">" or ">-" or "|" or "|-")
            {
                var folded = value.StartsWith('>');
                var block = new List<string>();
                while (index + 1 < end && (string.IsNullOrWhiteSpace(lines[index + 1]) || char.IsWhiteSpace(lines[index + 1][0])))
                    block.Add(lines[++index].Trim());
                value = string.Join(folded ? " " : "\n", block).Trim();
            }
            else if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
            {
                value = System.Text.Json.JsonSerializer.Deserialize(value, AiJsonContext.Default.String) ?? string.Empty;
            }
            else if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
            {
                value = value[1..^1].Replace("''", "'", StringComparison.Ordinal);
            }

            metadata[key] = value;
        }

        var name = metadata.GetValueOrDefault("name") ?? fallbackName;
        var description = metadata.GetValueOrDefault("description");
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(description) || name.Length > 128 || description.Length > 4000)
            throw new LauncherOperationException("AI_SKILL_METADATA_INVALID", "A skill needs bounded nonempty name and description metadata.");
        return new() { Id = id, Name = name, Description = description, Source = source, Version = Hash(body), Metadata = metadata, Body = body };
    }
    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
}