// Copyright (c) Richasy. All rights reserved.

using Rodel.Inference.Runtime.LlamaCpp;

namespace Rodel.MinecraftLauncher.Assistant;

/// <summary>Reads optional GGUF header facts without initializing inference or reading tensor data.</summary>
internal static class AiLocalModelMetadata
{
    /// <summary>Returns the declared context length when a readable GGUF supplies it; absent metadata never blocks a model request.</summary>
    public static int? ReadContextLength(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path)) return null;
        try
        {
            using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            var length = GgufMetadata.Read(stream).GetInt64("context_length");
            return length is > 0 and <= int.MaxValue ? (int)length.Value : null;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or GgufFormatException or ArgumentException)
        {
            return null;
        }
    }
}