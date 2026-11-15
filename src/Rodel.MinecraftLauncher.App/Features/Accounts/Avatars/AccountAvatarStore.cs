// Copyright (c) Richasy. All rights reserved.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

/// <summary>Stores a local account's appearance separately from its authentication and player identity.</summary>
/// <param name = "Preset">Packaged image identifier, or null for a copied custom PNG.</param>
/// <param name = "File">Owned immutable PNG filename; never a player-supplied source path.</param>
internal sealed record AccountAvatarPreference(string? Preset = "steve", string? File = null);

[JsonSerializable(typeof(AccountAvatarPreference))]
internal partial class AccountAvatarJsonContext : JsonSerializerContext;

/// <summary>Commits immutable image files before the single per-account preference value.</summary>
internal sealed class AccountAvatarStore(IDictionary<string, object> values, string directory, IReadOnlyCollection<string> presets)
{
    private static string Key(string id) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(id)));

    internal AccountAvatarPreference Read(string id)
    {
        if (!values.TryGetValue(Key(id), out var raw))
            return new();
        var value = JsonSerializer.Deserialize(raw as string ?? throw new InvalidDataException(), AccountAvatarJsonContext.Default.AccountAvatarPreference) ?? throw new InvalidDataException();
        if (value.File is { } file)
        {
            if (!Guid.TryParseExact(Path.GetFileNameWithoutExtension(file), "N", out _) || Path.GetFileName(file) != file || Path.GetExtension(file) != ".png" || !File.Exists(Path.Combine(directory, file)))
                throw new InvalidDataException();
        }
        else if (!presets.Contains(value.Preset, StringComparer.Ordinal))
        {
            throw new InvalidDataException();
        }

        return value;
    }

    internal string FilePath(AccountAvatarPreference preference) => Path.Combine(directory, preference.File ?? throw new InvalidOperationException());

    internal void SavePreset(string id, string preset)
    {
        if (!presets.Contains(preset, StringComparer.Ordinal))
            throw new ArgumentException("Unknown avatar.", nameof(preset));
        Commit(id, new(preset));
    }

    internal async Task SaveCustomAsync(string id, byte[] png, CancellationToken ct)
    {
        var file = Guid.NewGuid().ToString("N") + ".png";
        Directory.CreateDirectory(directory);
        var destination = Path.Combine(directory, file);
        var temporary = destination + ".tmp";
        var committed = false;
        try
        {
            await File.WriteAllBytesAsync(temporary, png, ct);
            ct.ThrowIfCancellationRequested();
            File.Move(temporary, destination);
            Commit(id, new(null, file));
            committed = true;
        }
        finally
        {
            TryDelete(temporary);
            if (!committed)
                TryDelete(destination);
        }
    }

    private void Commit(string id, AccountAvatarPreference preference)
    {
        AccountAvatarPreference? previous = null;
        try
        {
            previous = Read(id);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException or IOException)
        {
        }

        values[Key(id)] = JsonSerializer.Serialize(preference, AccountAvatarJsonContext.Default.AccountAvatarPreference);
        if (previous?.File is { } file && file != preference.File)
            TryDelete(Path.Combine(directory, file));
    }

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}