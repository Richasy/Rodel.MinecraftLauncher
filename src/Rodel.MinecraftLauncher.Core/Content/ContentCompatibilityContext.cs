// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Separates a launch profile ID from its Minecraft content compatibility version.</summary>
internal static class ContentCompatibilityContext
{
    /// <summary>Resolves inherited Minecraft and loader filters while retaining recoverable metadata warnings.</summary>
    public static async Task<ContentCompatibility> ResolveAsync(InstanceRecord instance, JsonNode settings, string? minecraftVersion, string? loader, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var launchVersion = settings["versionId"]?.GetValue<string>() ?? instance.VersionId;
        var result = new ContentCompatibility
        {
            LaunchVersion = launchVersion,
            MinecraftVersion = minecraftVersion ?? launchVersion,
            Loader = loader ?? settings["loader"]?.GetValue<string>(),
            Source = minecraftVersion is null ? "launch-version-fallback" : "explicit"
        };
        if (launchVersion is null)
            return result;
        var active = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = launchVersion;
        try
        {
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                if (current.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || current is "." or "..")
                    throw new InvalidDataException("Version metadata has an invalid profile reference: " + current);
                if (!active.Add(current))
                    throw new InvalidDataException("Version inheritance cycle at " + current);
                var file = Path.Combine(instance.MinecraftDirectory, "versions", current, current + ".json");
                if (!File.Exists(file))
                {
                    // The child's explicit parent ID remains useful even before vanilla files are installed.
                    if (minecraftVersion is null)
                    {
                        result.MinecraftVersion = current;
                        result.Source = current == launchVersion ? "launch-version-fallback" : "inherited-metadata-reference";
                    }

                    result.Warnings.Add("Version metadata is not installed for " + current + "; using the known profile reference. An explicit Minecraft filter remains available.");
                    return result;
                }

                var stream = File.OpenRead(file);
                await using var streamDisposal = stream.ConfigureAwait(false);
                var metadata = await JsonNode.ParseAsync(stream, documentOptions: new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip }, cancellationToken: ct).ConfigureAwait(false) as JsonObject ?? throw new InvalidDataException("Version metadata must be an object: " + current);
                if (loader is null && result.LoaderSource != "version-metadata" && Launch.RuntimeProfile.Loader(metadata).Kind is { } detected)
                {
                    result.Loader = detected;
                    result.LoaderSource = "version-metadata";
                }

                if (metadata["inheritsFrom"]?.GetValue<string>() is { Length: > 0 } parent)
                {
                    current = parent;
                    continue;
                }

                if (minecraftVersion is null)
                {
                    result.MinecraftVersion = metadata["id"]?.GetValue<string>() ?? current;
                    result.Source = active.Count > 1 ? "inherited-metadata" : "version-metadata";
                }

                return result;
            }
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or UnauthorizedAccessException or JsonException or InvalidOperationException or ArgumentException)
        {
            if (minecraftVersion is null)
            {
                result.MinecraftVersion = launchVersion;
                result.Source = "launch-version-fallback";
            }

            result.Warnings.Add("Could not resolve a base Minecraft version: " + ex.Message + ". The selected profile remains usable; supply an explicit Minecraft filter when needed.");
            return result;
        }
    }
}