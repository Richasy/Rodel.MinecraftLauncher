// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

public sealed partial class LauncherClient
{
    /// <summary>Reads shader inventory and actual Iris, Oculus or OptiFine selection without changing configuration.</summary>
    public Task<ShaderPackSelectionSummary> GetShaderPacksAsync(string instanceId, string? engine = null, CancellationToken cancellationToken = default)
        => ReadShaderPacksAsync(instanceId, engine, cancellationToken);

    /// <summary>Selects one installed shader pack or disables shaders, preserving unrelated Java properties and a recoverable original.</summary>
    public async Task<ShaderPackSelectionResult> SelectShaderPackAsync(string instanceId, ShaderPackSelectionOptions options, CancellationToken cancellationToken = default)
    {
        var instance = Instances.Get(instanceId);
        EnsureInstanceIdle(instanceId);
        var selection = await ReadShaderPacksAsync(instanceId, options.Engine, cancellationToken).ConfigureAwait(false);
        var engine = selection.Engine ?? throw new LauncherOperationException("SHADER_ENGINE_REQUIRED", "No unambiguous shader configuration owner was found. Install a shader engine or explicitly select iris, oculus or optifine.");
        var config = ShaderConfigPath(instance.GameDirectory, engine);
        using var lease = await ResourceLease.AcquireAsync(config, cancellationToken).ConfigureAwait(false);
        var bytes = File.Exists(config) ? await File.ReadAllBytesAsync(config, cancellationToken).ConfigureAwait(false) : [];
        var fingerprint = ShaderFingerprint(bytes);
        if (fingerprint != (options.ExpectedFingerprint ?? selection.Fingerprint))
            throw new LauncherOperationException("SHADER_CONFIG_CHANGED", "The shader configuration changed. Read its current selection before applying this operation.");
        var requested = string.IsNullOrEmpty(options.FileName) ? null : options.FileName;
        var entry = requested is null ? null : selection.Installed.FirstOrDefault(item => item.FileName == requested
            || (!item.Enabled && item.FileName == requested + ".disabled"));
        if (requested is not null && entry is null) throw new FileNotFoundException("The selected shader pack is not installed in this instance.", requested);
        var name = entry is null ? null : entry.Enabled ? entry.FileName : entry.ActiveFileName ?? entry.FileName[..^9];
        var document = new ShaderProperties(bytes);
        document.Set("shaderPack", name ?? (engine == "optifine" ? "OFF" : string.Empty));
        if (engine != "optifine") document.Set("enableShaders", name is not null ? "true" : "false");
        var updated = document.Bytes();
        if (options.PlanOnly)
            return new("planned", selection with { Selected = name, Enabled = name is not null, Present = entry is not null }, null);
        if (entry is { Enabled: false })
            await Content.SetEnabledAsync(instance.GameDirectory, "shaderpacks", entry.FileName, true, ct: cancellationToken).ConfigureAwait(false);
        if (bytes.AsSpan().SequenceEqual(updated)) return new("unchanged", await ReadShaderPacksAsync(instanceId, engine, cancellationToken).ConfigureAwait(false), null);

        EnsureInstanceIdle(instanceId);
        Directory.CreateDirectory(Path.GetDirectoryName(config)!);
        var temporary = config + "." + Guid.NewGuid().ToString("N") + ".tmp";
        string? recovery = null;
        try
        {
            await File.WriteAllBytesAsync(temporary, updated, cancellationToken).ConfigureAwait(false);
            var current = File.Exists(config) ? await File.ReadAllBytesAsync(config, cancellationToken).ConfigureAwait(false) : [];
            if (ShaderFingerprint(current) != fingerprint)
                throw new LauncherOperationException("SHADER_CONFIG_CHANGED", "The shader configuration changed before publication; the external edit was retained.");
            cancellationToken.ThrowIfCancellationRequested();
            if (File.Exists(config))
            {
                var directory = Path.Combine(instance.GameDirectory, ".rodel", "shader-settings");
                Directory.CreateDirectory(directory);
                recovery = Path.Combine(directory, engine + "-" + Guid.NewGuid().ToString("N") + ".properties");
                File.Replace(temporary, config, recovery);
            }
            else
            {
                File.Move(temporary, config);
            }
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }

        return new("applied", await ReadShaderPacksAsync(instanceId, engine, cancellationToken).ConfigureAwait(false), recovery);
    }

    private async Task<ShaderPackSelectionSummary> ReadShaderPacksAsync(string instanceId, string? requestedEngine, CancellationToken ct)
    {
        var instance = Instances.Get(instanceId);
        var installed = (await Content.ListAsync(instance.GameDirectory, "shaderpacks", ct: ct).ConfigureAwait(false)).Where(entry => entry.IsContent).ToArray();
        var engines = new HashSet<string>(StringComparer.Ordinal);
        foreach (var candidate in new[] { "iris", "oculus", "optifine" })
            if (File.Exists(ShaderConfigPath(instance.GameDirectory, candidate))) engines.Add(candidate);
        var mods = await Content.ListAsync(instance.GameDirectory, "mods", ct: ct).ConfigureAwait(false);
        var active = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mod in mods.Where(entry => entry.Enabled))
        {
            var metadata = mod.Metadata is JsonArray array ? array.OfType<JsonObject>() : mod.Metadata is JsonObject value ? [value] : [];
            foreach (var item in metadata)
            {
                var id = item["id"]?.ToString() ?? item["modId"]?.ToString();
                if (id is "iris" or "oculus" or "optifine") { engines.Add(id); active.Add(id); }
            }
        }

        var engine = requestedEngine ?? (active.Count == 1 ? active.Single() : engines.Count == 1 ? engines.Single() : null);
        if (engine is not null and not ("iris" or "oculus" or "optifine")) throw new ArgumentException("Shader engine must be iris, oculus or optifine.", nameof(requestedEngine));
        var warnings = new List<string>();
        if (engine is null) warnings.Add(engines.Count > 1 ? "Multiple shader configuration owners exist; select the intended engine." : "No shader engine configuration or recognized installed engine mod was found.");
        var file = engine is null ? null : ShaderConfigPath(instance.GameDirectory, engine);
        var bytes = file is not null && File.Exists(file) ? await File.ReadAllBytesAsync(file, ct).ConfigureAwait(false) : [];
        var document = new ShaderProperties(bytes);
        var name = document.Get("shaderPack");
        if (name is "" or "OFF" or "(internal)") name = null;
        var enabled = name is not null && (engine == "optifine" || string.Equals(document.Get("enableShaders") ?? "true", "true", StringComparison.OrdinalIgnoreCase));
        var present = name is not null && installed.Any(entry => entry.Enabled && entry.FileName == name);
        if (name is not null && !present) warnings.Add("The selected shader pack is not currently available in this instance.");
        return new(installed, engine, [.. engines.Order(StringComparer.Ordinal)], name, enabled, present, ShaderFingerprint(bytes), warnings);
    }

    private static string ShaderConfigPath(string game, string engine) => engine switch
    {
        "iris" => Path.Combine(game, "config", "iris.properties"),
        "oculus" => Path.Combine(game, "config", "oculus.properties"),
        "optifine" => Path.Combine(game, "optionsshaders.txt"),
        _ => throw new ArgumentException("Unknown shader configuration owner.", nameof(engine))
    };
    private static string ShaderFingerprint(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes));
}