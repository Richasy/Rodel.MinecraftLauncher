// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed record AiFileText(byte[] Bytes, string Text, Encoding Encoding, byte[] Preamble, string Hash, bool Redacted);

internal sealed class AiFileSnapshot(LauncherPaths paths, string staging, string? secret, long? minimumBytes = null, long? maximumBytes = null)
{
    public JsonArray Warnings { get; } = [];
    public int Entries { get; private set; }
    public int SourceBytes { get; private set; }
    private static readonly HashSet<string> SecretNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "launcher_accounts.json", "accounts.json", "auth.json", "credentials.json", ".credentials.json",
        ".claude.json", ".env", ".npmrc", ".netrc", "id_rsa", "id_ed25519", "accessTokens.json"
    };

    public string Target(string root, string relative)
    {
        if (Path.IsPathRooted(relative) || relative.Contains(':') || relative.Any(char.IsControl)
            || relative.Split('\\', '/').Any(part => part is "." or ".." || part.EndsWith('.') || part.EndsWith(' ')))
        {
            throw new ArgumentException("File tools accept a selected root and relative path, not absolute paths, traversal or alternate streams.");
        }

        var target = Path.GetFullPath(Path.Combine(root, relative));
        if (Protected(target))
            throw new LauncherOperationException("AI_FILE_PROTECTED", "This path contains product control, account or credential storage and is excluded even under a broad directory grant.");
        return target;
    }

    private bool Protected(string target)
    {
        var ai = Path.Combine(paths.Root, "ai");
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var codex = Environment.GetEnvironmentVariable("CODEX_HOME") ?? Path.Combine(profile, ".codex");
        var claude = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR") ?? Path.Combine(profile, ".claude");
        var protectedRoots = new[]
        {
            ai, AppControlService.RegistryDirectory(paths), Path.Combine(profile, ".ssh"), Path.Combine(profile, ".aws"), Path.Combine(profile, ".azure"),
            Path.Combine(codex, "sessions"), Path.Combine(codex, "history.jsonl"), Path.Combine(codex, "config.toml"),
            Path.Combine(claude, "projects"), Path.Combine(claude, "settings.json")
        };
        return SecretNames.Contains(Path.GetFileName(target))
            || protectedRoots.Any(root => target.Equals(root, StringComparison.OrdinalIgnoreCase)
                || target.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase));
    }

    public AiFileText Read(string root, string relative)
    {
        try { return ReadCore(root, relative); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherOperationException("AI_FILE_READ_FAILED", "The selected source file could not be read. No file content was supplied.",
                new JsonObject { ["path"] = relative, ["cause"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
        }
    }

    private AiFileText ReadCore(string root, string relative)
    {
        _ = Target(root, relative);
        using var opened = AiScopedFiles.Open(root, relative);
        if (opened.Stream.Length > 256000)
        {
            throw new LauncherOperationException("AI_FILE_TOO_LARGE", "The file exceeds the current 256 KB snapshot limit. No truncated text was treated as a complete file.",
                new JsonObject
                {
                    ["path"] = relative,
                    ["sizeBytes"] = opened.Stream.Length,
                    ["maximumBytes"] = 256000,
                    ["noTextReturned"] = true,
                    ["recovery"] = "Choose a smaller text file or explicitly prepare a bounded excerpt outside protected storage."
                });
        }

        var bytes = new byte[checked((int)opened.Stream.Length)];
        opened.Stream.ReadExactly(bytes);
        var encoding = bytes.AsSpan().StartsWith(new byte[] { 0xff, 0xfe }) ? (Encoding)new UnicodeEncoding(false, true, true)
            : bytes.AsSpan().StartsWith(new byte[] { 0xfe, 0xff }) ? new UnicodeEncoding(true, true, true)
            : new UTF8Encoding(bytes.AsSpan().StartsWith(new byte[] { 0xef, 0xbb, 0xbf }), true);
        var preamble = encoding.GetPreamble();
        try
        {
            var text = encoding.GetString(bytes, preamble.Length, bytes.Length - preamble.Length);
            if (text.Contains('\0')) throw new DecoderFallbackException();
            var scrubbed = SecretScrubber.Scrub(text, secret);
            return new(bytes, scrubbed, encoding, preamble, Convert.ToHexString(SHA256.HashData(bytes)), scrubbed != text);
        }
        catch (DecoderFallbackException)
        {
            throw new LauncherOperationException("AI_FILE_ENCODING_UNSUPPORTED", "The file is not strict UTF-8/UTF-16 text. No replacement decoding or binary text was supplied.",
                new JsonObject
                {
                    ["path"] = relative,
                    ["sizeBytes"] = bytes.Length,
                    ["noTextReturned"] = true,
                    ["supportedEncodings"] = new JsonArray("utf-8", "utf-16le-bom", "utf-16be-bom")
                });
        }
    }

    public string Stage(string relative, string text)
    {
        var target = Path.GetFullPath(Path.Combine(staging, relative));
        if (!target.StartsWith(staging + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("A staged file must remain under the owned snapshot directory.");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.WriteAllText(target, text, new UTF8Encoding(false, true));
        return target;
    }

    public void StageDirectory(string root, string relative, bool contents, bool recursive, CancellationToken ct)
    {
        try { Walk(root, relative, contents, recursive, 0, ct); }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            throw new LauncherOperationException("AI_DIRECTORY_READ_FAILED", "The selected directory snapshot could not be prepared. Other conversation capabilities remain available.",
                new JsonObject { ["path"] = relative, ["cause"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
        }
    }

    private void Walk(string root, string relative, bool contents, bool recursive, int depth, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var target = Target(root, relative);
        using var directory = AiScopedFiles.OpenDirectory(root, relative);
        Directory.CreateDirectory(Path.Combine(staging, relative));
        foreach (var entry in Directory.EnumerateFileSystemEntries(target))
        {
            ct.ThrowIfCancellationRequested();
            if (Entries >= 500 || SourceBytes >= 1048576)
            {
                Warnings.Add((JsonNode)new JsonObject { ["code"] = "AI_FILE_SCAN_BOUNDED", ["directory"] = relative });
                return;
            }

            Entries++;
            var child = Path.GetRelativePath(root, entry);
            if (Protected(entry) || Path.GetFileName(entry).Contains(".rodel-", StringComparison.OrdinalIgnoreCase))
            {
                Warnings.Add((JsonNode)new JsonObject { ["code"] = "AI_FILE_PROTECTED", ["path"] = child });
                continue;
            }

            try
            {
                if ((File.GetAttributes(entry) & FileAttributes.Directory) != 0)
                {
                    using var pin = AiScopedFiles.OpenDirectory(root, child);
                    Directory.CreateDirectory(Path.Combine(staging, child));
                    if (recursive && depth < 8) Walk(root, child, contents, true, depth + 1, ct);
                    else if (recursive) Warnings.Add((JsonNode)new JsonObject { ["code"] = "AI_FILE_DEPTH_BOUNDED", ["path"] = child });
                }
                else if (contents)
                {
                    var text = Read(root, child);
                    if (text.Bytes.LongLength < minimumBytes || text.Bytes.LongLength > maximumBytes) continue;
                    if (SourceBytes + text.Bytes.Length > 1048576)
                    {
                        Warnings.Add((JsonNode)new JsonObject { ["code"] = "AI_FILE_SCAN_BOUNDED", ["path"] = child });
                        return;
                    }

                    SourceBytes += text.Bytes.Length;
                    Stage(child, text.Text);
                }
                else
                {
                    using var pin = AiScopedFiles.Open(root, child);
                    if (pin.Stream.Length < minimumBytes || pin.Stream.Length > maximumBytes) continue;
                    Stage(child, string.Empty);
                }
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException or LauncherOperationException)
            {
                var failure = LauncherOperationException.ToResult(error);
                Warnings.Add((JsonNode)new JsonObject
                {
                    ["path"] = child,
                    ["error"] = failure["error"]!.DeepClone(),
                    ["details"] = failure["data"]?.DeepClone()
                });
            }
        }
    }

    public static byte[] Encode(string text, AiFileText? original)
    {
        var encoded = (original?.Encoding ?? new UTF8Encoding(false, true)).GetBytes(text);
        return original is null ? encoded : [.. original.Preamble, .. encoded];
    }
}