// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

internal sealed class AiManagedFileTools(LauncherPaths paths, JsonObject turn, AiManagedComponents components,
    string? secret, Func<string, string?, JsonObject, Task> emit) : IAsyncDisposable
{
    private readonly AiToolPolicy _policy = new(paths, turn);
    private readonly AiToolJournal _journal = new(paths);
    private readonly Dictionary<string, AiFileRootReader> _roots = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (string Actual, AiManagedFileIdentity Identity)> _tools = new(StringComparer.Ordinal);
    private readonly string _workspace = Path.Combine(paths.Root, "ai", "work", AiChatStore.CheckId(turn["id"]!.ToString()), "files");
    private AiManagedMcpSession? _session;
    private static bool Writes(string actual) => actual is "write_file" or "edit_file";
    private static string Hash(JsonNode value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value.ToJsonString())));

    public async Task<IReadOnlyList<AIFunctionDeclaration>> ConnectAsync(CancellationToken ct)
    {
        if (turn["fileAccessEnabled"]?.GetValue<bool>() != true) return [];
        try
        {
            _policy.CheckManagedFile(false, ct);
            var state = await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false);
            foreach (var root in AiFileRootReader.ReadConfigured(state))
                _roots.Add(root.Id, root);
            var (component, executable) = await components.OpenAsync("rodel_files", ct).ConfigureAwait(false);
            Directory.CreateDirectory(_workspace);
            using var pin = AiScopedFiles.OpenDirectory(_workspace);
            _session = await AiManagedMcpSession.ConnectAsync(new AiMcpRegistration
            {
                Id = component.Id,
                DisplayName = "Rodel managed files",
                ExecutablePath = executable,
                WorkingDirectory = _workspace,
                Arguments = ["--allow-write", _workspace]
            }, emit, ct: ct).ConfigureAwait(false);
            var offered = new List<AIFunctionDeclaration>();
            foreach (var tool in _session.Tools)
            {
                var actual = _session.ActualName(tool.Name);
                if (actual is not ("read_file_lines" or "list_directory" or "search_files" or "search_files_content" or "write_file" or "edit_file")) continue;
                var schema = AiToolDeclarations.Schema(tool);
                schema["properties"]!["rootId"] = new JsonObject
                {
                    ["type"] = new JsonArray("string", "null"),
                    ["description"] = "An authorized root ID for a relative path. Omit for an absolute local path; the host requests consent automatically when needed."
                };
                if (Writes(actual))
                {
                    schema["properties"]!["expectedSha256"] = new JsonObject
                    {
                        ["type"] = "string",
                        ["description"] = "Exact original byte SHA256 from a prior read; use 'absent' only when creating a new file."
                    };
                    schema["required"]!.AsArray().Add((JsonNode)JsonValue.Create("expectedSha256")!);
                }

                _tools.Add(tool.Name, (actual, new AiManagedFileIdentity(component.Id, actual, component.ExecutableSha256, Hash(schema))));
                offered.Add(AiToolDeclarations.Create(tool.Name,
                    tool.Description + " Paths may be relative to rootId or absolute local paths. Outside granted roots, the host automatically pauses for path consent; do not use ask_user to request file permission or tell users to configure a root first. Core supplies a bounded, sanitized snapshot to this real MCP tool; only a guarded Core transaction can change the original. Reads support strict UTF-8/UTF-16. Directory scans are bounded (500 entries, depth 8, about 1 MB). File writes require both read_write and operation permission. Authorized roots: "
                        + new JsonArray(_roots.Values.Select(root => (JsonNode)root.Snapshot()).ToArray()).ToJsonString(), schema));
            }

            await emit("managed_mcp_ready", "managed:files", new JsonObject
            {
                ["id"] = component.Id,
                ["version"] = component.Version,
                ["revision"] = component.Revision,
                ["toolCount"] = offered.Count,
                ["processId"] = _session.ProcessId,
                ["rootCount"] = _roots.Count,
                ["policy"] = "Pinned source handles; community MCP receives owned snapshots, never credential/control directories."
            }).ConfigureAwait(false);
            return offered;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception error)
        {
            await emit("managed_mcp_unavailable", "managed:files", new JsonObject
            {
                ["id"] = "rodel_files",
                ["ordinaryConversationAvailable"] = true,
                ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
            }).ConfigureAwait(false);
            return [];
        }
    }

    public bool Contains(string name) => _tools.ContainsKey(name);

    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, string invocation, string callId, CancellationToken ct)
    {
        if (_session is null || !_tools.TryGetValue(name, out var tool))
            throw new LauncherOperationException("AI_MANAGED_MCP_UNAVAILABLE", "The managed file tool is not connected.");
        var write = Writes(tool.Actual) && !(tool.Actual == "edit_file" && arguments["dryRun"]?.GetValue<bool>() == true);
        if (write) await _policy.AuthorizeOperationAsync(name, arguments, invocation, emit, ct).ConfigureAwait(false);
        _policy.CheckManagedFile(write, ct);
        var relative = arguments["path"]?.GetValue<string>() ?? throw new ArgumentException("Specify a relative or absolute local path.");
        // A configured read-only root also uses the host gate for a requested write, rather than waiting for settings edits.
        if (write && !Path.IsPathRooted(relative) && arguments["rootId"]?.ToString() is { } requestedRoot
            && _roots.TryGetValue(requestedRoot, out var configuredRoot) && !configuredRoot.AllowsWrite)
        {
            relative = new AiFileSnapshot(paths, _workspace, secret).Target(configuredRoot.Path, relative);
        }

        AiFileRootReader root;
        if (Path.IsPathFullyQualified(relative))
        {
            var targetPath = Path.GetFullPath(relative);
            if (targetPath.StartsWith(@"\\", StringComparison.Ordinal)) throw new ArgumentException("Select a real local path.");
            var existing = _roots.Values.Where(item => item.Contains(targetPath) && (!write || item.AllowsWrite))
                .OrderByDescending(item => item.Path.Length).FirstOrDefault();
            if (existing is not null)
            {
                root = existing;
            }
            else
            {
                // Validate the path and protected targets before presenting consent; no source bytes are read here.
                var ancestor = Path.GetDirectoryName(targetPath) ?? Path.GetPathRoot(targetPath)!;
                while (!Directory.Exists(ancestor))
                {
                    ancestor = Path.GetDirectoryName(ancestor)
                    ?? throw new LauncherOperationException("AI_FILE_ROOT_UNAVAILABLE", "The target volume is unavailable.");
                }

                _ = new AiFileSnapshot(paths, _workspace, secret).Target(ancestor, Path.GetRelativePath(ancestor, targetPath));
                await new AiPathAccess(paths).AuthorizeAsync(turn, invocation, name, arguments, targetPath, write,
                    token => { _policy.CheckManagedFile(write, token); return Task.CompletedTask; }, emit, ct).ConfigureAwait(false);
                root = AiFileRootReader.ForPath(ancestor, targetPath, write);
            }

            relative = Path.GetRelativePath(root.Path, targetPath);
        }
        else
        {
            root = await SelectRootAsync(arguments["rootId"]?.ToString(), write, invocation, ct).ConfigureAwait(false);
        }

        var staging = Path.Combine(_workspace, invocation);
        Directory.CreateDirectory(staging);
        var minimum = arguments["min_bytes"]?.GetValue<long>();
        var maximum = arguments["max_bytes"]?.GetValue<long>();
        if (minimum < 0 || maximum < 0 || minimum > maximum) throw new ArgumentException("Use nonnegative original-byte size bounds, with minimum no greater than maximum.");
        var snapshot = new AiFileSnapshot(paths, staging, secret, minimum, maximum);
        var rootPath = root.Path;
        var directory = tool.Actual is "list_directory" or "search_files" or "search_files_content";
        if (directory && relative is "." or ".\\" or "./") relative = string.Empty;
        var target = snapshot.Target(rootPath, relative);
        relative = Path.GetRelativePath(rootPath, target);
        if (relative == ".") relative = string.Empty;
        arguments["rootId"] = root.ApprovedPath is null ? root.IdValue() : null;
        arguments["path"] = root.ApprovedPathValue() ?? JsonValue.Create(relative);
        var sourceIdentity = tool.Identity with { RootPath = rootPath };
        await CheckRootAsync(root, write, ct).ConfigureAwait(false);
        if (write && _journal.ReuseRecorded(turn, name, arguments, sourceIdentity) is { } prior) return prior;
        var grant = new AiCallGrant(token => CheckRootAsync(root, write, token), ct);
        await using var grantLifetime = grant.ConfigureAwait(false);
        AiFileText? original = null;
        if (!directory && (string.IsNullOrWhiteSpace(relative) || Path.EndsInDirectorySeparator(relative)))
            throw new ArgumentException("Select a file relative to the granted directory.");
        if (directory)
        {
            snapshot.StageDirectory(rootPath, relative, tool.Actual == "search_files_content", tool.Actual != "list_directory", grant.Token);
        }
        else if (File.Exists(target))
        {
            original = snapshot.Read(rootPath, relative);
            snapshot.Stage(relative, original.Text);
        }
        else if (tool.Actual != "write_file")
        {
            throw new LauncherOperationException("AI_FILE_NOT_FOUND", "The requested file does not exist in its authorized root. List the directory or correct the relative path.");
        }

        if (!directory) Directory.CreateDirectory(Path.GetDirectoryName(Path.Combine(staging, relative))!);
        if (write && (original?.Redacted == true || !string.Equals(arguments["expectedSha256"]?.ToString(),
            original?.Hash ?? "absent", StringComparison.OrdinalIgnoreCase)))
        {
            throw new LauncherOperationException(original?.Redacted == true ? "AI_FILE_REDACTED_WRITE" : "AI_FILE_REVISION_CONFLICT",
                original?.Redacted == true ? "This file contains masked credentials; automatic edits cannot reconstruct its private bytes."
                    : "The file differs from the expected original revision. Read it again before planning a new patch.");
        }

        var supplied = arguments.DeepClone().AsObject();
        supplied.Remove("rootId");
        supplied.Remove("expectedSha256");
        supplied.Remove("min_bytes");
        supplied.Remove("max_bytes");
        supplied["path"] = Path.Combine(staging, relative);
        if (tool.Actual == "read_file_lines")
        {
            var limit = supplied["limit"]?.GetValue<int>() ?? 200;
            if (limit is < 1 or > 400) throw new ArgumentException("Read one through 400 lines per call.");
            supplied["limit"] = limit;
        }

        if (tool.Actual == "write_file" && (supplied["content"]?.ToString().Length ?? 0) > 128000)
            throw new ArgumentException("A file write may contain at most 128000 UTF-16 characters.");
        var expectedPatch = tool.Actual == "edit_file" && original is not null
            ? ExactPatch(original.Text, supplied, write) : null;
        await CheckRootAsync(root, write, ct).ConfigureAwait(false);
        AiMcpCallResult response;
        try
        {
            response = await _session.CallAsync(name, supplied, grant.Token, allowReadRecovery: !write,
                authorize: token => CheckRootAsync(root, write, token)).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested && grant.Revocation is not null)
        {
            throw new LauncherOperationException("AI_FILE_ACCESS_REVOKED", "The directory or file capability changed. The source read was canceled and its data was withheld.");
        }

        await grant.StopAsync().ConfigureAwait(false);
        await CheckRootAsync(root, write, ct).ConfigureAwait(false);
        var result = JsonNode.Parse(JsonSerializer.Serialize(response, AssistantJsonContext.Default.AiMcpCallResult).Replace(staging.Replace("\\", "\\\\", StringComparison.Ordinal),
            "<selected-root>", StringComparison.Ordinal))!.AsObject();
        var fileState = new AiFileObservation
        {
            RootId = root.ApprovedPath is null ? root.IdValue() : null,
            Path = root.ApprovedPathValue() ?? JsonValue.Create(relative),
            Sha256 = original?.Hash,
            OriginalBytes = original?.Bytes.Length,
            ByteOrderMarkBytes = original?.Preamble.Length,
            Encoding = original?.Encoding.WebName,
            Redacted = original?.Redacted ?? false,
            Offset = arguments["offset"]?.DeepClone(),
            Limit = supplied["limit"]?.DeepClone(),
            Warnings = snapshot.Warnings.DeepClone().AsArray()
        };
        result["fileState"] = JsonSerializer.SerializeToNode(fileState, AiToolObservationJsonContext.Default.AiFileObservation);
        if (write && response.Status == "succeeded")
        {
            var changed = AiScopedFiles.ReadText(staging, relative, 512000);
            if (expectedPatch is not null && changed != expectedPatch)
                throw new LauncherOperationException("AI_FILE_PATCH_RESULT_CHANGED", "The native edit differed from the exact planned text or line endings. No source file was changed.");
            var bytes = AiFileSnapshot.Encode(changed, original);
            await CommitAsync(name, arguments, invocation, callId, sourceIdentity, root, relative, bytes, original, fileState, result, ct).ConfigureAwait(false);
        }

        await emit("file_result", invocation, EventResult(result)).ConfigureAwait(false);
        return result;
    }

    public static JsonObject EventResult(JsonObject result)
    {
        var visible = result.DeepClone().AsObject();
        if (visible["data"] is JsonObject data)
        {
            data.Remove("content");
            data.Remove("structuredContent");
        }

        visible["fileBodyOmittedFromEvent"] = true;
        return visible;
    }

    internal static string ExactPatch(string original, JsonObject arguments, bool write)
    {
        static string Normalize(string value) => value.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
        var newline = original.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : original.Contains('\r') ? "\r" : "\n";
        string Render(string value) => newline == "\n" ? value : value.Replace("\n", newline, StringComparison.Ordinal);
        var expected = Normalize(original);
        if (write && Render(expected) != original)
            throw new LauncherOperationException("AI_FILE_MIXED_NEWLINES", "The native patch engine normalizes mixed line endings. Read/preview remains available; no source bytes were changed. Use an explicit full replacement if that normalization is intended.");
        var edits = arguments["edits"] as JsonArray ?? throw new ArgumentException("Provide exact text edits.");
        if (edits.Count is < 1 or > 64) throw new ArgumentException("Provide one through 64 bounded text edits.");
        foreach (var edit in edits)
        {
            var oldText = Normalize(edit?["oldText"]?.GetValue<string>() ?? string.Empty);
            var newText = Normalize(edit?["newText"]?.GetValue<string>() ?? string.Empty);
            if (oldText.Length == 0 || oldText.Length + newText.Length > 128000)
                throw new ArgumentException("An edit needs bounded, nonempty original text.");
            var start = expected.IndexOf(oldText, StringComparison.Ordinal);
            if (start < 0) throw new LauncherOperationException("AI_FILE_PATCH_NO_EXACT_MATCH", "The patch text does not match exactly. Whitespace-fuzzy replacement is not permitted.");
            var all = arguments["replaceAll"]?.GetValue<bool>() == true;
            if (!all && expected.IndexOf(oldText, start + oldText.Length, StringComparison.Ordinal) >= 0)
                throw new LauncherOperationException("AI_FILE_PATCH_AMBIGUOUS", "The original text has multiple matches. Narrow the exact patch or explicitly request replaceAll.");
            expected = all ? expected.Replace(oldText, newText, StringComparison.Ordinal) : expected[..start] + newText + expected[(start + oldText.Length)..];
        }

        return Render(expected);
    }

    private async Task<AiFileRootReader> SelectRootAsync(string? id, bool write, string invocation, CancellationToken ct)
    {
        var selected = id is null ? _roots.Count == 1 ? _roots.Values.Single() : null : _roots.GetValueOrDefault(id);
        if (selected is not null && (!write || selected.AllowsWrite)) return selected;
        var store = new AiChatStore(paths);
        var previousStatus = turn["status"]!.DeepClone();
        var pending = new JsonObject
        {
            ["invocationId"] = invocation,
            ["rootId"] = id,
            ["mode"] = write ? "read_write" : "read_only",
            ["reason"] = selected is null ? "select_directory" : "read_write_required",
            ["resume"] = "This waiting invocation resumes after the matching explicit directory/mode grant. Stop the turn to decline."
        };
        turn["pendingAccess"] = pending;
        turn["status"] = "awaiting_access";
        await store.SaveTurnAsync(turn, ct).ConfigureAwait(false);
        await emit("access_required", invocation, pending).ConfigureAwait(false);
        try
        {
            while (true)
            {
                _policy.CheckManagedFile(write, ct);
                var current = await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false);
                var roots = AiFileRootReader.ReadConfigured(current);
                selected = id is null ? roots.Length == 1 ? roots[0] : null : roots.SingleOrDefault(root => root.Id == id);
                if (selected is not null && (!write || selected.AllowsWrite))
                {
                    _roots[selected.Id] = selected;
                    await emit("access_granted", invocation, new JsonObject
                    {
                        ["rootId"] = selected.IdValue(),
                        ["revision"] = current["revision"]!.DeepClone(),
                        ["mode"] = selected.ModeValue(),
                        ["resumedWaitingInvocation"] = true
                    }).ConfigureAwait(false);
                    return selected;
                }

                await Task.Delay(150, ct).ConfigureAwait(false);
            }
        }
        finally
        {
            turn.Remove("pendingAccess");
            turn["status"] = previousStatus;
            await store.SaveTurnAsync(turn, CancellationToken.None).ConfigureAwait(false);
        }
    }

    private async Task CheckRootAsync(AiFileRootReader root, bool write, CancellationToken ct)
    {
        _policy.CheckManagedFile(write, ct);
        if (root.ApprovedPath is not null)
        {
            if (!Directory.Exists(root.Path))
                throw new LauncherOperationException("AI_FILE_ROOT_UNAVAILABLE", "The approved path's volume or ancestor is no longer available.");
            return;
        }

        var current = await new AiExtensionStore(paths).ReadDocumentAsync(ct).ConfigureAwait(false);
        var found = AiFileRootReader.ReadConfigured(current).SingleOrDefault(item => item.Id == root.Id);
        if (found is null || !string.Equals(found.Path, root.Path, StringComparison.OrdinalIgnoreCase)
            || (write && (!root.AllowsWrite || !found.AllowsWrite)))
        {
            throw new LauncherOperationException("AI_FILE_ROOT_REVOKED", "The selected directory or write grant changed; pending source data was withheld.");
        }

        if (!Directory.Exists(root.Path))
            throw new LauncherOperationException("AI_FILE_ROOT_UNAVAILABLE", "The selected directory is no longer readable or present. Correct its path or permissions; no source data was supplied.");
    }

    internal async Task CommitAsync(string name, JsonObject arguments, string invocation, string callId, AiManagedFileIdentity identity,
        AiFileRootReader root, string relative, byte[] bytes, AiFileText? original, AiFileObservation fileState, JsonObject result, CancellationToken ct)
    {
        if (bytes.Length > 256000) throw new LauncherOperationException("AI_FILE_TOO_LARGE", "The proposed encoded file exceeds the bounded text-file transaction size.");
        var rootPath = root.Path;
        var snapshot = new AiFileSnapshot(paths, _workspace, secret);
        var target = snapshot.Target(rootPath, relative);
        using var lease = await ResourceLease.AcquireAsync(target, ct).ConfigureAwait(false);
        var current = File.Exists(target) ? snapshot.Read(rootPath, relative) : null;
        if (current?.Hash != original?.Hash)
            throw new LauncherOperationException("AI_FILE_REVISION_CONFLICT", "The source changed while the MCP prepared its edit; nothing was committed. Read the new revision.");
        var parentPath = Path.GetDirectoryName(target)!;
        var temporary = Path.Combine(parentPath, ".rodel-" + invocation + ".tmp");
        var backup = Path.Combine(parentPath, ".rodel-" + invocation + ".bak");
        JsonObject? intent = null;
        var dispatched = false;
        var sourcePhase = false;
        try
        {
            intent = await _journal.PrepareAsync(turn, invocation, callId, name, arguments, ct, identity).ConfigureAwait(false);
            var desired = Convert.ToHexString(SHA256.HashData(bytes));
            intent["fileTransaction"] = new JsonObject
            {
                ["rootId"] = root.IdValue(),
                ["rootPath"] = rootPath,
                ["path"] = relative,
                ["expectedSha256"] = original?.Hash ?? "absent",
                ["desiredSha256"] = desired,
                ["createsParentDirectories"] = !Directory.Exists(parentPath),
                ["pathAccess"] = root.ApprovedPathValue(),
                ["backupPath"] = original is null ? null : Path.GetRelativePath(rootPath, backup)
            };
            await _journal.DispatchingAsync(turn, intent, ct).ConfigureAwait(false);
            await CheckRootAsync(root, true, ct).ConfigureAwait(false);
            current = File.Exists(target) ? snapshot.Read(rootPath, relative) : null;
            if (current?.Hash != original?.Hash)
                throw new LauncherOperationException("AI_FILE_REVISION_CONFLICT", "The source changed before commit; nothing was overwritten.");
            dispatched = true;
            sourcePhase = true;
            using var directory = AiScopedFiles.EnsureDirectory(rootPath, Path.GetDirectoryName(relative) ?? string.Empty);
            var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            await using (output.ConfigureAwait(false))
            {
                await output.WriteAsync(bytes, ct).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }

            if (original is null) File.Move(temporary, target, overwrite: false);
            else File.Replace(temporary, target, backup, ignoreMetadataErrors: false);
            if (original is not null && snapshot.Read(rootPath, Path.GetRelativePath(rootPath, backup)).Hash != original.Hash)
                throw new LauncherOperationException("AI_FILE_COMMIT_RACE", "Another process replaced the file during the final commit window. Its bytes remain in the recorded backup. Inspect both revisions before further changes.");
            var observed = snapshot.Read(rootPath, relative);
            if (observed.Hash != desired)
                throw new LauncherOperationException("AI_FILE_POST_COMMIT_DRIFT", "The file changed again after commit. The previous revision remains in the recorded backup; inspect current bytes before continuing.");
            fileState.Sha256 = observed.Hash;
            fileState.CurrentBytes = observed.Bytes.Length;
            fileState.CurrentByteOrderMarkBytes = observed.Preamble.Length;
            fileState.PlayerFileModified = true;
            fileState.BackupPath = JsonSerializer.SerializeToElement(original is null ? null : Path.GetRelativePath(rootPath, backup), AiJsonContext.Default.String);
            fileState.DesiredStateObserved = observed.Hash == desired;
            result["fileState"] = JsonSerializer.SerializeToNode(fileState, AiToolObservationJsonContext.Default.AiFileObservation);
            sourcePhase = false;
            await _journal.CompleteAsync(turn, intent, result, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception error)
        {
            if (intent is not null)
            {
                if (dispatched) await _journal.UnknownAsync(turn, intent, error).ConfigureAwait(false);
                else await _journal.NotDispatchedAsync(turn, intent).ConfigureAwait(false);
            }

            if (sourcePhase && error is IOException or UnauthorizedAccessException)
            {
                throw new LauncherOperationException("AI_FILE_COMMIT_FAILED", "The source file could not be committed. Inspect the recorded effect before retrying.",
                    new JsonObject { ["path"] = relative, ["cause"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone() });
            }

            throw;
        }
        finally
        {
            try { if (File.Exists(temporary)) File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { /* Retain an uncommitted temporary file rather than masking the durable outcome. */ }
        }
    }

    public async ValueTask DisposeAsync()
    {
        try { if (_session is not null) await _session.DisposeAsync().ConfigureAwait(false); }
        finally
        {
            if (Directory.Exists(_workspace))
            {
                try { Directory.Delete(_workspace, recursive: true); }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    await emit("managed_mcp_cleanup_failed", "managed:files", new JsonObject
                    {
                        ["error"] = LauncherOperationException.ToResult(error)["error"]!.DeepClone()
                    }).ConfigureAwait(false);
                }
            }
        }
    }
}