// Copyright (c) Richasy. All rights reserved.

using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Launch;

public sealed partial class SessionService
{
    private static async Task PrepareLoggingAsync(JsonObject state)
    {
        var plan = state["loggingCompatibility"]?.Deserialize(LaunchJsonContext.Default.LoggingCompatibilityPlan);
        if (plan is null) return;
        foreach (var patch in plan.ArchiveCopies)
        {
            var source = patch.Source;
            var destination = patch.Destination;
            var temporary = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                var original = File.OpenRead(source);
                await using var originalDisposal = original.ConfigureAwait(false);
                var hash = Convert.ToHexStringLower(await SHA256.HashDataAsync(original).ConfigureAwait(false));
                if (hash != patch.SourceSha256)
                    throw new IOException("Logging source changed after planning; original classpath retained.");
                original.Position = 0;
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                using var input = new ZipArchive(original, ZipArchiveMode.Read, leaveOpen: true);
                {
                    var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 65536, true);
                    await using (output.ConfigureAwait(false))
                    {
                        using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                        {
                            foreach (var entry in input.Entries)
                            {
                                var signature = entry.FullName.StartsWith("META-INF/", StringComparison.OrdinalIgnoreCase) && SignatureExtensions.Any(ext => entry.FullName.EndsWith(ext, StringComparison.OrdinalIgnoreCase));
                                if (entry.FullName == patch.RemoveEntry || signature)
                                    continue;
                                var copy = archive.CreateEntry(entry.FullName, CompressionLevel.Fastest);
                                var from = entry.Open();
                                await using var fromDisposal = from.ConfigureAwait(false);
                                var to = copy.Open();
                                await using var toDisposal = to.ConfigureAwait(false);
                                await from.CopyToAsync(to).ConfigureAwait(false);
                            }
                        }

                        await output.FlushAsync().ConfigureAwait(false);
                        output.Flush(true);
                    }
                }

                File.Move(temporary, destination, true);
                patch.Status = "prepared";
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
            {
                var arguments = state["arguments"]!.AsArray().Select(x => x!.ToString()).ToList();
                LoggingCompatibility.RewritePaths(arguments, state["mainClassArgumentIndex"]?.GetValue<int>() ?? arguments.Count, destination, source);
                state["arguments"] = new JsonArray([.. arguments.Select(x => (JsonNode?)JsonValue.Create(x))]);
                patch.Status = "original-fallback";
                patch.Warning = ex.Message;
                (state["warnings"] as JsonArray)?.Add((JsonNode?)JsonValue.Create("Logging compatibility copy failed; original library retained: " + ex.Message));
            }
            finally
            {
                if (File.Exists(temporary))
                    File.Delete(temporary);
            }

            state["loggingCompatibility"] = JsonSerializer.SerializeToNode(plan, LaunchJsonContext.Default.LoggingCompatibilityPlan);
        }
    }

    private static readonly string[] SignatureExtensions = [".SF", ".RSA", ".DSA", ".EC"];
}