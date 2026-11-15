// Copyright (c) Richasy. All rights reserved.

using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Java;

public sealed partial record JavaVersionInfo(int Major, int Minor, int Security, int Build, bool Prerelease) : IComparable<JavaVersionInfo>
{
    /// <summary>Parses legacy and modern Java version strings, preserving build numbers and prerelease ordering.</summary>
    /// <param name="text">The version value reported by a Java runtime.</param>
    /// <returns>The parsed version, or null when no supported numeric prefix is present.</returns>
    public static JavaVersionInfo? Parse(string? text)
    {
        if (text is null)
            return null;
        var match = VersionPrefixRegex().Match(text);
        if (!match.Success || !int.TryParse(match.Groups[1].Value, out var major))
            return null;
        int Number(int i) => int.TryParse(match.Groups[i].Value, out var n) ? n : 0;
        var suffix = match.Groups[5].Value;
        var legacyBuild = LegacyBuildRegex().Match(suffix);
        var build = legacyBuild.Success && int.TryParse(legacyBuild.Groups[1].Value, out var legacy) ? legacy : Number(4);
        var prerelease = PrereleaseRegex().IsMatch(suffix);
        return new(major, Number(2), Number(3), build, prerelease);
    }

    public int CompareTo(JavaVersionInfo? other)
    {
        if (other is null)
            return 1;
        foreach (var pair in new[]
        {
            (Major, other.Major),
            (Minor, other.Minor),
            (Security, other.Security),
            (Prerelease ? 0 : 1, other.Prerelease ? 0 : 1),
            (Build, other.Build)
        }

        )
        {
            if (pair.Item1.CompareTo(pair.Item2) is var result && result != 0)
                return result;
        }

        return 0;
    }

    [GeneratedRegex(@"^(?:1\.)?(\d+)(?:\.(\d+))?(?:[._](\d+))?(?:\+(\d+))?([\w.-]*)", RegexOptions.CultureInvariant)]
    private static partial Regex VersionPrefixRegex();

    [GeneratedRegex(@"^-b(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex LegacyBuildRegex();

    [GeneratedRegex(@"^-(?:ea|internal|alpha|beta|rc|preview)(?:[-.]|$)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex PrereleaseRegex();
}