// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Content;

/// <summary>Optional future provider. Project/file/version results use CurseForge fields without a data envelope.</summary>
public interface ICurseForgeProvider
{
    /// <summary>Search may retain its data array; project, file and version results are unwrapped.</summary>
    public Task<JsonObject> SearchAsync(string query, string projectType, CancellationToken ct = default);
    public Task<JsonObject> GetProjectAsync(string projectId, CancellationToken ct = default);
    public Task<JsonArray> GetVersionsAsync(string projectId, string? minecraftVersion = null, string? loader = null, CancellationToken ct = default);
    public Task<JsonObject> GetFileAsync(string projectId, string fileId, CancellationToken ct = default);

    /// <summary>Called for every request and redirect. Return credentials only for explicitly trusted HTTPS targets;
    /// return empty headers elsewhere. The launcher supplies no credential reader or default provider.</summary>
    public IReadOnlyDictionary<string, string> GetDownloadHeaders(Uri uri);
}