// Copyright (c) Richasy. All rights reserved.

using System.Runtime.InteropServices;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Launch;

public static class MetadataRules
{
    /// <summary>Applies rules in source order, with the last matching action deciding inclusion; absent rules remain unrestricted.</summary>
    public static bool Allows(JsonNode? rules, IReadOnlyDictionary<string, bool>? features = null, string? javaArchitecture = null)
    {
        var entries = MetadataRuleReader.ReadAll(rules);
        if (entries is null)
            return true;
        var allowed = false;
        foreach (var rule in entries)
        {
            var matches = true;
            if (rule.HasOperatingSystem())
            {
                var name = rule.GetOperatingSystemName();
                if (name is not null and not "windows")
                    matches = false;
                var arch = rule.GetArchitecture();
                var runtimeArch = NormalizeArchitecture(javaArchitecture ?? RuntimeInformation.OSArchitecture.ToString());
                if (arch is not null && NormalizeArchitecture(arch) != runtimeArch)
                    matches = false;
                var version = rule.GetOperatingSystemVersionPattern();
                if (version is not null && !Regex.IsMatch(Environment.OSVersion.Version.ToString(), version, RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1)))
                    matches = false;
                var (Minimum, Maximum) = rule.GetOperatingSystemVersionRange();
                var current = Environment.OSVersion.Version;
                if (Version.TryParse(Minimum, out var minimum) && current < minimum)
                    matches = false;
                if (Version.TryParse(Maximum, out var maximum) && current > maximum)
                    matches = false;
            }

            foreach (var (name, required) in rule.GetRequiredFeatures())
            {
                if ((features?.GetValueOrDefault(name) ?? false) != required)
                    matches = false;
            }

            if (matches)
                allowed = rule.GetAction() == "allow";
        }

        return allowed;
    }

    internal static bool AllowsAnyJavaArchitecture(JsonNode? rules)
    {
        var architectures = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "x86", "x64", "arm64" };
        foreach (var rule in MetadataRuleReader.ReadAll(rules) ?? [])
        {
            if (rule.GetArchitecture() is { } architecture)
                architectures.Add(architecture);
        }

        return architectures.Any(architecture => Allows(rules, javaArchitecture: architecture));
    }

    private static string NormalizeArchitecture(string architecture) => architecture.ToLowerInvariant() switch
    {
        "amd64" or "x86_64" or "x64" => "x64",
        "i386" or "i486" or "i586" or "i686" or "x86" => "x86",
        "aarch64" or "arm64" => "arm64",
        var other => other
    };
    /// <summary>Converts a Maven coordinate, optional classifier and extension into its relative repository path.</summary>
    public static string MavenPath(string coordinate)
    {
        var extensionParts = coordinate.Split('@', 2);
        var parts = extensionParts[0].Split(':');
        if (parts.Length < 3)
            throw new InvalidDataException($"Invalid Maven coordinate '{coordinate}'.");
        var classifier = parts.Length > 3 ? "-" + parts[3] : string.Empty;
        var extension = extensionParts.Length > 1 ? extensionParts[1] : "jar";
        return $"{parts[0].Replace('.', '/')}/{parts[1]}/{parts[2]}/{parts[1]}-{parts[2]}{classifier}.{extension}";
    }
}