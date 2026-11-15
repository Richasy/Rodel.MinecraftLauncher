// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Known legacy Log4j classpath copies; shared originals and explicit logging configuration remain intact.</summary>
public static class LoggingCompatibility
{
    public static LaunchPlan Configure(LaunchPlan plan, JsonObject metadata, bool enabled)
    {
        var arguments = plan.Arguments.ToList();
        var index = plan.MainClassArgumentIndex ?? arguments.IndexOf(plan.MainClass);
        var patches = new List<LoggingArchiveCopy>();
        var warnings = plan.Warnings.ToList();
        if (enabled)
        {
            foreach (var property in new[]
            {
                "java.rmi.server.useCodebaseOnly=true",
                "com.sun.jndi.rmi.object.trustURLCodebase=false",
                "com.sun.jndi.cosnaming.object.trustURLCodebase=false",
                "log4j2.formatMsgNoLookups=true"
            }

            )
            {
                var key = "-D" + property.Split('=')[0] + "=";
                if (!arguments.Take(index).Any(x => x.StartsWith(key, StringComparison.Ordinal)))
                    arguments.Insert(index++, "-D" + property);
            }

            foreach (var library in (metadata["libraries"] as JsonArray ?? []).OfType<JsonObject>())
            {
                var parts = library["name"]?.ToString().Split(':') ?? [];
                if (parts.Length < 3 || parts[0] != "org.apache.logging.log4j" || parts[1] != "log4j-core")
                    continue;
                var known = parts[2] == "2.0-beta9" || (Version.TryParse(parts[2], out var version) && version >= new Version(2, 0) && version <= new Version(2, 14, 1));
                if (!known)
                    continue;
                var relative = library["downloads"]?["artifact"]?["path"]?.ToString() ?? MetadataRules.MavenPath(library["name"]!.ToString());
                var file = plan.RequiredFiles.FirstOrDefault(f => f.Kind == "library" && f.Path.EndsWith(relative.Replace('/', Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase));
                if (file is null || !file.Exists)
                {
                    warnings.Add("Legacy Log4j classpath copy will be prepared after its source library is available.");
                    continue;
                }

                string hash;
                try
                {
                    using var input = File.OpenRead(file.Path);
                    hash = Convert.ToHexStringLower(SHA256.HashData(input));
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    warnings.Add("Logging source could not be inspected; original classpath retained: " + ex.Message);
                    continue;
                }

                // Preserve the jar filename so automatic Java module names do not change.
                var destination = Path.Combine(plan.WorkingDirectory, ".rodel", "logging", hash, Path.GetFileName(file.Path));
                if (!RewritePaths(arguments, index, file.Path, destination))
                {
                    warnings.Add("Legacy logging library is not on a recognized launch class/module path; original arguments retained.");
                    continue;
                }

                patches.Add(new(file.Path, hash, destination, "org/apache/logging/log4j/core/lookup/JndiLookup.class", library["name"]!.GetValue<string>()));
            }
        }

        return plan with
        {
            Arguments = [.. arguments],
            MainClassArgumentIndex = index,
            Warnings = [.. warnings],
            LoggingCompatibility = new(enabled, patches)
        };
    }

    public static bool RewritePaths(IList<string> arguments, int targetIndex, string source, string destination)
    {
        var changed = false;
        string Rewrite(string value) => string.Join(Path.PathSeparator, value.Split(Path.PathSeparator).Select(x =>
        {
            if (!string.Equals(x, source, StringComparison.OrdinalIgnoreCase))
                return x;
            changed = true;
            return destination;
        }));
        for (var i = 0; i < targetIndex; i++)
        {
            if (arguments[i] is "-cp" or "-classpath" or "--class-path" or "-p" or "--module-path")
            {
                if (i + 1 < targetIndex)
                {
                    arguments[i + 1] = Rewrite(arguments[i + 1]);
                    i++;
                }
            }
            else
            {
                foreach (var prefix in new[]
                {
                    "--class-path=",
                    "--module-path=",
                    "-DlegacyClassPath="
                }

                )
                {
                    if (arguments[i].StartsWith(prefix, StringComparison.Ordinal))
                    {
                        arguments[i] = prefix + Rewrite(arguments[i][prefix.Length..]);
                        break;
                    }
                }
            }
        }

        return changed;
    }
}