// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using O = Rodel.MinecraftLauncher.Cli.Assistant.AssistantOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal static class AssistantInput
{
    /// <summary>Requires the explicit revision used to protect a configuration or conversation edit.</summary>
    public static long Revision(ParseResult parsed) => parsed.GetValue(O.Revision) ?? throw new ArgumentException(S.RevisionRequired);
    /// <summary>Reads a mandatory typed option without interpreting its value as another command.</summary>
    public static string Required(ParseResult parsed, Option<string?> option) => parsed.GetValue(option) ?? throw new ArgumentException(string.Format(S.ValueRequired, option.Name.TrimStart('-')));
    /// <summary>Selects at most one model binding for an execution operation.</summary>
    public static string? Model(ParseResult parsed)
    {
        var models = parsed.GetValue(O.Models) ?? [];
        return models.Length switch { 0 => null, 1 => models[0], _ => throw new ArgumentException(S.SingleModelRequired) };
    }
    /// <summary>Reads a bounded UTF-8 request document using the caller's source-generated JSON contract.</summary>
    public static async Task<T> JsonAsync<T>(string file, JsonTypeInfo<T> type, CancellationToken ct)
        => JsonSerializer.Deserialize(await TextAsync(file, 128000, ct).ConfigureAwait(false), type) ?? throw new ArgumentException(S.InvalidArguments);
    /// <summary>Reads strict UTF-8 text and rejects oversized input instead of silently truncating it.</summary>
    public static async Task<string> TextAsync(string file, int maximum, CancellationToken ct)
    {
        using var reader = new StreamReader(Path.GetFullPath(file), new UTF8Encoding(false, true));
        return await ReadBoundedAsync(reader, maximum, ct).ConfigureAwait(false);
    }
    /// <summary>Reads exactly one prompt source; MCP protocol stdin and conflicting sources are rejected before host dispatch.</summary>
    public static async Task<string> PromptAsync(ParseResult parsed, bool allowsStandardInput, string? fallback, CancellationToken ct)
    {
        var prompt = parsed.GetValue(O.Prompt);
        var file = parsed.GetValue(O.PromptFile);
        var stdin = parsed.GetValue(O.PromptStdin);
        var count = (prompt is null ? 0 : 1) + (file is null ? 0 : 1) + (stdin ? 1 : 0);
        if (count > 1) throw new ArgumentException(S.PromptSourceConflict);
        if (stdin)
        {
            if (!allowsStandardInput) throw new ArgumentException(S.PromptStdinDirectOnly);
            if (!Console.IsInputRedirected) throw new ArgumentException(S.PromptStdinRedirected);
            return await ReadBoundedAsync(Console.In, 64000, ct).ConfigureAwait(false);
        }

        return file is not null ? await TextAsync(file, 64000, ct).ConfigureAwait(false)
            : prompt ?? fallback ?? throw new ArgumentException(S.PromptRequired);
    }
    private static async Task<string> ReadBoundedAsync(TextReader reader, int maximum, CancellationToken ct)
    {
        var buffer = new char[maximum + 1];
        var length = 0;
        while (length < buffer.Length)
        {
            var count = await reader.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
            if (count == 0) break;
            length += count;
        }

        return length > maximum ? throw new ArgumentException(S.InputTooLarge) : new string(buffer, 0, length);
    }
    /// <summary>Reads an explicitly selected protected credential source, never consuming MCP's protocol stdin.</summary>
    public static async Task<string?> CredentialAsync(ParseResult parsed, bool allowed, CancellationToken ct)
    {
        if (!allowed && (parsed.GetValue(O.CredentialStdin) || parsed.GetValue(O.CredentialPrompt))) throw new ArgumentException(S.SecretInputDirectOnly);
        if (parsed.GetValue(O.CredentialStdin) && parsed.GetValue(O.CredentialPrompt)) throw new ArgumentException(S.SecretInputConflict);
        if (parsed.GetValue(O.CredentialStdin))
        {
            var buffer = new char[16387];
            var length = 0;
            while (length < buffer.Length)
            {
                var count = await Console.In.ReadAsync(buffer.AsMemory(length), ct).ConfigureAwait(false);
                if (count == 0) break;
                length += count;
            }

            return length >= buffer.Length ? throw new ArgumentException(S.InputTooLarge) : new string(buffer, 0, length).TrimEnd('\r', '\n');
        }

        if (!parsed.GetValue(O.CredentialPrompt)) return null;
        if (Console.IsInputRedirected) throw new ArgumentException(S.SecretInputRedirected);
        Console.Error.Write(S.SecretPrompt);
        var value = new StringBuilder();
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) { Console.Error.WriteLine(); return value.ToString(); }

            if (key.Key == ConsoleKey.Escape) throw new OperationCanceledException(ct);
            if (key.Key == ConsoleKey.Backspace) { if (value.Length > 0) value.Length--; }
            else if (!char.IsControl(key.KeyChar))
            {
                if (value.Length >= 16384) throw new ArgumentException(S.InputTooLarge);
                value.Append(key.KeyChar);
            }
        }
    }
}