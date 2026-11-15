// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Storage;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed record AiManagedManifest(int SchemaVersion, string Platform, AiManagedComponent[] Components);
internal sealed record AiManagedComponent(string Id, string Version, string Repository, string Revision,
    string License, string Executable, string ExecutableSha256);

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true)]
[JsonSerializable(typeof(AiManagedManifest))]
internal sealed partial class AiManagedJsonContext : JsonSerializerContext;

internal sealed class AiManagedComponents : IDisposable
{
    private readonly List<AiScopedFiles.Scope> _pins = [];
    private readonly string _base;

    internal AiManagedComponents(string? packageRoot = null) => _base = packageRoot ?? AppContext.BaseDirectory;

    /// <summary>Verifies and pins the packaged component for this process architecture until this owner is disposed.</summary>
    public async Task<(AiManagedComponent Component, string Executable)> OpenAsync(string id, CancellationToken ct)
    {
        var path = Path.Combine(_base, "Assets", "AiMcp", "runtime.json");
        if (!File.Exists(path))
            throw new LauncherOperationException("AI_MANAGED_MCP_PACKAGE_MISSING", "The installed package lacks its managed MCP components. Repair or update this installation; ordinary model conversation remains available.");
        var manifest = JsonFiles.ReadObject(path).Deserialize(AiManagedJsonContext.Default.AiManagedManifest)
            ?? throw new InvalidDataException("The managed MCP manifest is empty.");
        var platform = RuntimeInformation.ProcessArchitecture switch
        {
            Architecture.X64 => "win-x64",
            Architecture.Arm64 => "win-arm64",
            _ => null
        };
        if (manifest.SchemaVersion != 1 || manifest.Platform != platform)
            throw new LauncherOperationException("AI_MANAGED_MCP_VERSION_UNSUPPORTED", "The installed managed MCP manifest has an unsupported format/platform.");
        var component = manifest.Components.SingleOrDefault(item => item.Id == id)
            ?? throw new LauncherOperationException("AI_MANAGED_MCP_PACKAGE_MISSING", "The installed package omitted a required managed component.");
        var relative = Path.Combine("Assets", "AiMcp", component.Executable);
        var scope = AiScopedFiles.Open(_base, relative);
        try
        {
            var hash = Convert.ToHexString(await SHA256.HashDataAsync(scope.Stream, ct).ConfigureAwait(false));
            if (!string.Equals(hash, component.ExecutableSha256, StringComparison.OrdinalIgnoreCase))
                throw new LauncherOperationException("AI_MANAGED_MCP_HASH_MISMATCH", "The installed managed MCP executable does not match its verified package manifest.");
            _pins.Add(scope);
        }
        catch { scope.Dispose(); throw; }

        return (component, Path.GetFullPath(Path.Combine(_base, relative)));
    }

    /// <inheritdoc />
    public void Dispose()
    {
        foreach (var scope in _pins) scope.Dispose();
    }
}