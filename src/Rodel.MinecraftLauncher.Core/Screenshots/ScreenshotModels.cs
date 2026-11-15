// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Screenshots;

/// <summary>Describes one independent image and the file metadata observed when it was selected.</summary>
/// <param name="FileName">The image's single file name inside the instance screenshot directory.</param>
/// <param name="Path">The absolute image path, also binding a selection to its original instance directory.</param>
/// <param name="Bytes">The observed file length.</param>
/// <param name="ModifiedUtc">The observed last-write time, not an inferred capture time or world identity.</param>
public sealed record ScreenshotFile(string FileName, string Path, long Bytes, DateTimeOffset ModifiedUtc);

/// <summary>Preserves a file-specific discovery failure without hiding other images.</summary>
/// <param name="FileName">The image that could not be inspected.</param>
/// <param name="Message">The original filesystem error.</param>
public sealed record ScreenshotWarning(string FileName, string Message);

/// <summary>Reports the actual instance screenshot directory and its readable image entries.</summary>
/// <param name="Directory">The absolute directory, including when it has not been created yet.</param>
/// <param name="Exists">Whether the directory was available during enumeration.</param>
/// <param name="Files">Images ordered by last-write time descending, then file name.</param>
/// <param name="Warnings">Individual metadata failures retained alongside usable images.</param>
public sealed record ScreenshotCatalog(string Directory, bool Exists, IReadOnlyList<ScreenshotFile> Files, IReadOnlyList<ScreenshotWarning> Warnings);

/// <summary>Reports the actual outcome for one selected image without rolling back successful independent files.</summary>
/// <param name="FileName">The selected original image name.</param>
/// <param name="Destination">The published copy or recycle-bin path; null for permanent deletion or failure.</param>
/// <param name="ErrorCode">A stable failure category; null on success.</param>
/// <param name="Error">The original failure detail; null on success.</param>
public sealed record ScreenshotFileResult(string FileName, string? Destination, string? ErrorCode = null, string? Error = null);

/// <summary>Retains per-file effects and cancellation when a batch only partially completes.</summary>
/// <param name="Items">Outcomes for attempted files, including actual destinations.</param>
/// <param name="Canceled">Whether cancellation stopped the batch before all requested files were attempted.</param>
public sealed record ScreenshotBatchResult(IReadOnlyList<ScreenshotFileResult> Items, bool Canceled)
{
    /// <summary>Gets the number of files successfully copied, recycled or permanently deleted.</summary>
    public int Completed => Items.Count(item => item.ErrorCode is null);

    /// <summary>Gets whether every requested operation completed successfully.</summary>
    public bool Succeeded => !Canceled && Items.All(item => item.ErrorCode is null);
}

/// <summary>Provides NativeAOT-safe metadata for screenshot API and CLI results.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ScreenshotCatalog))]
[JsonSerializable(typeof(ScreenshotFile))]
[JsonSerializable(typeof(ScreenshotBatchResult))]
public sealed partial class ScreenshotJsonContext : JsonSerializerContext;