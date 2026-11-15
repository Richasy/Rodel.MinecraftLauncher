// Copyright (c) Richasy. All rights reserved.

using System.Text;

namespace Rodel.MinecraftLauncher.App.Features.SeedExplorer;

/// <summary>Reads App-owned editor drafts asynchronously and commits a complete snapshot before its window closes.</summary>
internal sealed class SeedWorkspacePreferences(string localPath)
{
    private readonly string _path = Path.Combine(localPath, "SeedExplorer", "draft.json");
    internal bool HasSavedDraft { get; private set; }

    internal Task<SeedWorkspaceDraft> LoadAsync(CancellationToken ct) => Task.Run(async () =>
    {
        try
        {
            HasSavedDraft = File.Exists(_path);
            return HasSavedDraft ? SeedWorkspaceDraft.Read(await File.ReadAllTextAsync(_path, ct).ConfigureAwait(false)) : new();
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Seed editor preferences could not be read: {0}", error);
            return new();
        }
    }, ct);

    internal void Save(SeedWorkspaceDraft draft)
    {
        var temporary = _path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                var bytes = Encoding.UTF8.GetBytes(draft.Serialize());
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporary, _path, overwrite: true);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            System.Diagnostics.Trace.TraceWarning("Seed editor preferences could not be saved: {0}", error);
        }
        finally
        {
            try { File.Delete(temporary); }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
        }
    }
}