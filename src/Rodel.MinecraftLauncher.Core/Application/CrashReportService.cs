// Copyright (c) Richasy. All rights reserved.

using System.Text;
using System.Text.RegularExpressions;

namespace Rodel.MinecraftLauncher.Core.Application;

public static partial class CrashReportService
{
    private const int Budget = 1024 * 1024;
    /// <summary>Analyzes a bounded crash report while retaining the supplied game context for diagnosis.</summary>
    public static async Task<CrashAnalysis> AnalyzeAsync(string file, string? encoding = null, CancellationToken ct = default)
    {
        file = Path.GetFullPath(file);
        var stream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 65536, FileOptions.Asynchronous);
        await using var streamDisposal = stream.ConfigureAwait(false);
        var buffer = new byte[(int)Math.Min(stream.Length, Budget)];
        var read = 0;
        while (read < buffer.Length)
        {
            ct.ThrowIfCancellationRequested();
            var count = await stream.ReadAsync(buffer.AsMemory(read), ct).ConfigureAwait(false);
            if (count == 0)
                break;
            read += count;
        }

        if (encoding is null)
        {
            try
            {
                new UTF8Encoding(false, true).GetString(buffer, 0, read);
                encoding = "utf-8";
            }
            catch (DecoderFallbackException)
            {
                encoding = Launch.LogEncoding.SystemEncoding;
            }
        }

        var codec = Launch.LogEncoding.Resolve(encoding, out var encodingWarning);
        var lines = codec.GetString(buffer, 0, read).Split('\n').Select(x => x.TrimEnd('\r')).ToArray();
        var result = Analyze(lines);
        return result with { Path = file, BytesRead = read, TotalBytes = stream.Length, Truncated = stream.Length > read, Encoding = codec.WebName, EncodingWarning = encodingWarning };
    }

    /// <summary>Indexes bounded source lines into advisory findings and recognized report evidence.</summary>
    public static CrashAnalysis Analyze(IEnumerable<string> input)
    {
        var findings = new List<DiagnosticFinding>();
        var details = new CrashDetails();
        var suspects = new List<DependencyEvidence>();
        var found = new HashSet<string>();
        var modLines = new HashSet<string>();
        foreach (var original in input)
        {
            var line = original.Length > 2048 ? original[..2048] : original;
            var trimmed = line.Trim();
            foreach (var key in new[]
            {
                "Description",
                "Minecraft Version",
                "Java Version",
                "Java VM Version",
                "Operating System",
                "Launched Version"
            }

            )
            {
                if (trimmed.StartsWith(key + ":", StringComparison.OrdinalIgnoreCase))
                {
                    var value = trimmed[(key.Length + 1)..].Trim();
                    switch (key)
                    {
                        case "Description": details.Description = value; break;
                        case "Minecraft Version": details.MinecraftVersion = value; break;
                        case "Java Version": details.JavaVersion = value; break;
                        case "Java VM Version": details.JavaVmVersion = value; break;
                        case "Operating System": details.OperatingSystem = value; break;
                        case "Launched Version": details.LaunchedVersion = value; break;
                    }
                }
            }

            var classVersion = ClassVersion().Match(line);
            if (classVersion.Success && int.TryParse(classVersion.Groups[1].Value, out var major) && major >= 45)
            {
                details.RequiredJavaMajor = Math.Max(details.RequiredJavaMajor ?? 0, major - 44);
                details.ClassVersionEvidence = line;
                var responsibleClass = Regex.Match(line, @"([\w.$/]+)\s+has been compiled by", RegexOptions.CultureInvariant, TimeSpan.FromSeconds(1));
                if (responsibleClass.Success) details.ResponsibleClass = responsibleClass.Groups[1].Value.Replace('/', '.');
            }

            var javaReplacement = FabricJavaReplacement().Match(line);
            if (javaReplacement.Success
                && int.TryParse(javaReplacement.Groups[1].Value, out var currentJava)
                && int.TryParse(javaReplacement.Groups[2].Value, out var requiredJava)
                && requiredJava > currentJava)
            {
                details.RequiredJavaMajor = Math.Max(details.RequiredJavaMajor ?? 0, requiredJava);
                details.JavaRequirementKind = "loader-dependency";
                if (found.Add("JAVA_VERSION_MISMATCH"))
                {
                    findings.Add(new("JAVA_VERSION_MISMATCH", line, original.Length > line.Length,
                        "Choose the Java version required by the loader; replacing mods is not the indicated fix.", "matched-evidence"));
                }
            }

            foreach (var (pattern, code, suggestion) in Rules)
            {
                if (line.Contains(pattern, StringComparison.OrdinalIgnoreCase) && found.Add(code))
                    findings.Add(new(code, line, original.Length > line.Length, suggestion, "matched-evidence"));
            }

            foreach (Match missing in FabricMissingDependency().Matches(line))
            {
                var requester = missing.Groups[2].Value;
                var dependency = missing.Groups[3].Value;
                if (suspects.Count < 40 && modLines.Add(requester + "\0" + dependency))
                {
                    suspects.Add(new FabricDependencyEvidence(missing.Groups[1].Success ? "missing-required-dependency" : "required-dependency",
                        dependency, requester, missing.Groups[4].Value, line));
                }
            }

            if (DependencyLine().IsMatch(line) && modLines.Add(line) && suspects.Count < 40)
            {
                var match = FabricMod().Match(line);
                var forge = ForgeDependency().Match(line);
                var olderFabric = OlderFabricDependency().Match(line);
                suspects.Add(new ModDependencyEvidence(line, match.Success ? match.Groups[1].Value : null,
                    match.Success ? match.Groups[2].Value : forge.Success ? forge.Groups[1].Value : olderFabric.Success ? olderFabric.Groups[2].Value : null,
                    forge.Success ? forge.Groups[2].Value : olderFabric.Success ? olderFabric.Groups[1].Value : null,
                    match.Success ? match.Groups[3].Value : forge.Success ? forge.Groups[3].Value : olderFabric.Success ? olderFabric.Groups[3].Value : null,
                    forge.Success ? forge.Groups[4].Value : null));
            }
        }

        return new() { Details = details, Findings = findings, DependencyEvidence = suspects };
    }

    private static readonly (string Pattern, string Code, string Suggestion)[] Rules = [("OutOfMemoryError", "JAVA_OUT_OF_MEMORY", "Review the heap limit, native memory and active mod set."), ("Could not reserve enough space", "JAVA_MEMORY_RESERVATION", "Reduce memory or choose a 64-bit Java runtime."), ("UnsupportedClassVersionError", "JAVA_VERSION_MISMATCH", "Use a Java version supporting the reported class-file version."), ("incompatible mod set", "MOD_DEPENDENCY_CONFLICT", "Follow the loader's named dependency and version requirements."), ("Incompatible mods found", "MOD_DEPENDENCY_CONFLICT", "Follow the loader's named dependency and version requirements."), ("Missing or unsupported mandatory dependencies", "MOD_DEPENDENCY_CONFLICT", "Install or adjust the named mandatory dependencies."), ("ModResolutionException", "MOD_DEPENDENCY_CONFLICT", "Follow the loader's dependency details; preserve local files when adjusting versions."), ("registry entries that are unknown to this client", "REGISTRY_MISMATCH", "Compare the exact server release and its required client content; do not upgrade to latest automatically."), ("Registry remapping failed", "REGISTRY_MISMATCH", "Compare the exact server release and its required client content."), ("MixinApplyError", "MIXIN_FAILURE", "Inspect the named mixin, target and supported game/mod versions."), ("MixinTransformerError", "MIXIN_FAILURE", "Inspect the nested mixin cause and named target."), ("ClassNotFoundException", "CLASS_NOT_FOUND", "Check the named class and responsible library/mod."), ("NoClassDefFoundError", "CLASS_NOT_FOUND", "Check the nested cause; the class may be missing or have failed initialization."), ("NoSuchMethodError", "BINARY_VERSION_MISMATCH", "Check game, loader and mod versions for the named method."), ("NoSuchFieldError", "BINARY_VERSION_MISMATCH", "Check game, loader and mod versions for the named field."), ("AccessDeniedException", "FILE_ACCESS_DENIED", "Inspect the named path, permissions and open file handles."), ("GLFW error 65542", "OPENGL_UNAVAILABLE", "Inspect graphics drivers and Java architecture."), ("EXCEPTION_ACCESS_VIOLATION", "JVM_NATIVE_CRASH", "Read the JVM problematic frame and driver/native library details."), ("Problematic frame:", "JVM_NATIVE_FRAME", "Inspect the following native/JVM frame; its presence alone does not identify the root cause.")];
    [GeneratedRegex(@"requires|depends on|is missing|mandatory dependenc|Mod ID:|Incompatible mods|incompatible mod set", RegexOptions.IgnoreCase)]
    private static partial Regex DependencyLine();
    [GeneratedRegex(@"Mod '([^']+)' \(([^)]+)\) (.+)")]
    private static partial Regex FabricMod();
    [GeneratedRegex(@"Mod ID: '([^']+)', Requested by: '([^']+)', Expected range: '([^']+)', Actual version: '([^']+)'")]
    private static partial Regex ForgeDependency();
    [GeneratedRegex(@"Mod (\S+) depends on mod \{(\S+) @ ([^}]+)\}")]
    private static partial Regex OlderFabricDependency();
    [GeneratedRegex(@"HARD_DEP(_NO_CANDIDATE)?\s+([a-z0-9_.-]+)\s+\S+\s+\{depends\s+([a-z0-9_.-]+)\s+@\s+\[([^\]]+)\]\}")]
    private static partial Regex FabricMissingDependency();
    [GeneratedRegex(@"Fix:\s+add\s+\[[^\r\n]*?\breplace\s+\[\[java\s+(\d+)(?:\.\d+)*\]\s*->\s*add:java\s+(\d+)\b")]
    private static partial Regex FabricJavaReplacement();
    [GeneratedRegex(@"(?:class file (?:major )?version|major(?:\.minor)? version)\s+(\d+)", RegexOptions.IgnoreCase)]
    private static partial Regex ClassVersion();
}