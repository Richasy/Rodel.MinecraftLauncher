// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Java;

/// <summary>Chooses Java candidates and describes their recommendations without performing installation or launching a game.</summary>
public static class JavaSelection
{
    /// <summary>Reads the metadata's recommended major version or infers an advisory preference from known game versions.</summary>
    public static int? PreferredMajor(JsonObject metadata)
    {
        if (metadata["javaVersion"]?["majorVersion"] is { } explicitMajor && int.TryParse(explicitMajor.ToString(), out var major))
            return major;
        if (metadata["mainClass"]?.ToString() == "net.minecraft.launchwrapper.Launch")
            return 8;
        var id = metadata["_minecraftVersion"]?.ToString() ?? metadata["id"]?.ToString();
        if (Version.TryParse(id, out var version))
        {
            if (version.Major >= 26)
                return 25;
            if (version.Major == 1)
                return version >= new Version(1, 20, 5) ? 21 : version.Minor >= 18 ? 17 : version.Minor == 17 ? 16 : 8;
        }

        return null;
    }

    /// <summary>Maps a known Java major version to Mojang's runtime component, or null for an unknown mapping.</summary>
    public static string? Component(int? major) => major switch
    {
        8 => "jre-legacy",
        16 => "java-runtime-alpha",
        17 => "java-runtime-beta",
        21 => "java-runtime-delta",
        25 => "java-runtime-epsilon",
        _ => null
    };
    /// <summary>Ranks usable executables by an existing major-version preference, compatibility advice, bitness and version without enforcing compatibility gates.</summary>
    public static JavaInstallation? Choose(IEnumerable<JavaInstallation> candidates, int? preferred, JsonObject? metadata = null) =>
        candidates.Where(candidate => candidate.IsUsable)
            .OrderByDescending(candidate => preferred is not null && candidate.MajorVersion == preferred)
            .ThenBy(candidate => metadata is null ? null : CompatibilityNote(metadata, candidate))
            .ThenByDescending(candidate => candidate.Bitness == 64)
            .ThenByDescending(candidate => JavaVersionInfo.Parse(candidate.FullVersion))
            .ThenByDescending(candidate => candidate.MajorVersion)
            .ThenBy(candidate => candidate.Path, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    /// <summary>Returns advice for known game/loader and Java update combinations while retaining explicit selections.</summary>
    public static string? CompatibilityNote(JsonObject metadata, JavaInstallation java)
    {
        var id = metadata["_minecraftVersion"]?.ToString() ?? metadata["id"]?.ToString();
        var number = JavaVersionInfo.Parse(java.FullVersion);
        if (number is null)
            return null;
        if (System.Version.TryParse(id, out var game) && game >= new System.Version(1, 13) && number.Major == 8 && number.Security < 51)
            return "Known Minecraft 1.13+ Java 8 world-generation issue before update 51; the selected Java is retained.";
        var detected = RuntimeProfile.Loader(metadata);
        var forge = detected.Version?.StartsWith(id + "-", StringComparison.Ordinal) == true ? detected.Version[(id!.Length + 1)..] : detected.Version;
        var affected = detected.Kind == "forge" && (id == "1.16.4" || (id == "1.16.3" && System.Version.TryParse(forge, out var lower) && lower >= new System.Version(34, 1, 27)) || (id == "1.16.5" && System.Version.TryParse(forge, out var upper) && upper <= new System.Version(36, 2, 23)) || (id == "1.17.1" && System.Version.TryParse(forge, out var range) && range >= new System.Version(37, 0, 60) && range <= new System.Version(37, 0, 75)));
        var issue = number.Major > 17 || (number.Major == 8 && number.Security >= 321) || (number.Major == 11 && number.CompareTo(JavaVersionInfo.Parse("11.0.14")) >= 0) || (number.Major == 15 && number.CompareTo(JavaVersionInfo.Parse("15.0.6")) >= 0) || (number.Major == 17 && number.CompareTo(JavaVersionInfo.Parse("17.0.2")) >= 0);
        return affected && issue ? "Known older Forge/ModLauncher JDK update compatibility issue; prefer an available compatible update or patched loader. Explicit selection remains allowed." : null;
    }

    /// <summary>Captures a typed candidate and diagnostic snapshot without mutating third-party metadata or serializing a protocol result.</summary>
    public static JavaSelectionReport Describe(IEnumerable<JavaInstallation> candidates, JavaInstallation? selected, JsonObject metadata, bool explicitPath)
    {
        var snapshot = candidates.ToArray();
        var preferred = PreferredMajor(metadata);
        var warnings = snapshot.SelectMany(candidate => candidate.Warnings).Concat(selected?.Warnings ?? []).Distinct(StringComparer.Ordinal).ToList();
        var declaredComponent = metadata["javaVersion"]?["component"];
        var component = Component(preferred);
        if (declaredComponent is JsonValue value && value.TryGetValue<string>(out var name))
            component = name;
        else if (declaredComponent is not null)
            warnings.Add("Java runtime component metadata is not a string; the known major-version mapping is used as advice.");
        return new JavaSelectionReport
        {
            PreferredMajor = preferred,
            RuntimeComponent = component,
            Source = explicitPath ? JavaSelectionSource.Explicit : metadata["javaVersion"] is not null
                ? JavaSelectionSource.Metadata : preferred is null ? JavaSelectionSource.AvailableFallback : JavaSelectionSource.KnownVersionPreference,
            SelectedPath = selected?.Path,
            SelectedMajor = selected?.MajorVersion,
            SelectedVersion = selected?.FullVersion,
            CompatibilityNote = selected is null ? null : CompatibilityNote(metadata, selected),
            Warnings = [.. warnings],
            Candidates = [.. snapshot.Select(candidate => new JavaSelectionCandidate
            {
                Path = candidate.Path,
                FullVersion = candidate.FullVersion,
                MajorVersion = candidate.MajorVersion,
                IsUsable = candidate.IsUsable,
                Architecture = candidate.Architecture,
                Bitness = candidate.Bitness,
                FileEncoding = candidate.FileEncoding,
                NativeEncoding = candidate.NativeEncoding
            })]
        };
    }
}