// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Java;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Installation;

/// <summary>The closed choice between an installation preview and the actual prepared-file result.</summary>
[JsonDerivedType(typeof(Launch.LaunchPlan))]
[JsonDerivedType(typeof(InstallationResult))]
public interface IInstallationResult;

/// <summary>Reports the files actually prepared by one game installation, optionally enriched with Java preparation.</summary>
public sealed record InstallationResult : IInstallationResult
{
    /// <summary>Gets the selected game version identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the resolved runtime directory that received the installation.</summary>
    public required string MinecraftDirectory { get; init; }
    /// <summary>Gets the number of artifacts downloaded during this attempt.</summary>
    public int Downloaded { get; init; }
    /// <summary>Gets the number of existing artifacts reused.</summary>
    public int Reused { get; init; }
    /// <summary>Gets whether native archives are available for extraction during launch.</summary>
    public bool NativeArchivesPrepared { get; init; }
    /// <summary>Gets the number of completed planned files.</summary>
    public int CompletedFiles { get; init; }
    /// <summary>Gets the total number of planned files.</summary>
    public int? TotalFiles { get; init; }
    /// <summary>Gets artifacts reused from shared runtime storage.</summary>
    public int SharedReusedFiles { get; init; }
    /// <summary>Gets shared cache candidates that could not be read.</summary>
    public int CacheReadFailures { get; init; }
    /// <summary>Gets an ancillary activity-log failure without invalidating the installation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActivityLogWarning { get; internal set; }
    /// <summary>Gets the Java selection made when the caller requested runtime preparation.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JavaSelectionReport? Java { get; internal set; }
    /// <summary>Gets a recoverable Java preparation failure when an existing runtime remains usable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaWarning { get; internal set; }
}