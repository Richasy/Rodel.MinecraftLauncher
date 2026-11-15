// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Storage;

namespace Rodel.MinecraftLauncher.Core.Skins;

/// <summary>Owns editable skin projects, cross-process transactions and atomic artifact publication.</summary>
public sealed class SkinService(LauncherPaths paths)
{
    /// <summary>The file extension for Rodel Minecraft Skin project documents.</summary>
    public const string ProjectFileExtension = ".rmcs";

    /// <summary>Gets candidate review and host-grant operations using this same project store.</summary>
    public SkinProposalService Proposals => new(paths);
    /// <summary>Gets stable built-in template identities for editor selection; hosts localize their labels.</summary>
    public static string[] Templates => SkinTemplates.Ids;

    /// <summary>Creates a project from an explicit template, opaque color or PNG; imports use their actual dimensions.</summary>
    public async Task<string> CreateAsync(SkinModel model, string? importPath = null, string color = "#808080",
        int size = 64, string? template = null, string name = "Untitled", CancellationToken cancellationToken = default)
    {
        if (importPath is not null && template is not null)
            throw new ArgumentException("Choose a template or PNG input, not both.");
        var document = importPath is not null ? await DecodeAsync(importPath, model, cancellationToken).ConfigureAwait(false)
            : template is not null ? SkinTemplates.Create(template, model, size) : SkinEditor.Create(model, color, size);
        var id = Guid.NewGuid().ToString("N");
        await SaveProjectAsync(id, new(document, CheckName(name), DateTimeOffset.UtcNow, []), cancellationToken).ConfigureAwait(false);
        return id;
    }

    /// <summary>Reads validated editable content; missing or damaged projects are never replaced with blank data.</summary>
    public SkinDocument Read(string id) => ReadProject(id).Document;

    /// <summary>Loads a cape's separately saved character as a frozen preview reference without changing either artwork.</summary>
    public SkinRenderSnapshot? ReadCompanion(string id)
    {
        var project = ReadProject(id);
        if (project.Document.Model != SkinModel.Cape || project.CompanionId is null) return null;
        var companion = Read(project.CompanionId);
        CheckPair(project.Document, companion);
        return new(companion);
    }

    /// <summary>Atomically stores or clears a preview association without changing either document's pixels, revision or history.</summary>
    /// <param name="id">The cape whose preview association is changed, or a work whose old association is cleared.</param>
    /// <param name="expectedRevision">The observed artwork revision, checked under its publication lease.</param>
    /// <param name="companionId">A managed character work, or null to remove the association.</param>
    /// <param name="cancellationToken">Cancels before metadata publication.</param>
    public async Task SetCompanionAsync(string id, long expectedRevision, string? companionId, CancellationToken cancellationToken = default)
    {
        if (companionId == id) throw new ArgumentException("A work cannot reference itself.", nameof(companionId));
        var companion = companionId is null ? null : Read(companionId);
        using var lease = await ResourceLease.AcquireAsync(DocumentPath(id), cancellationToken).ConfigureAwait(false);
        var project = ReadProject(id);
        if (project.Document.Revision != expectedRevision) throw new InvalidOperationException("The work changed. Refresh before selecting a companion.");
        if (companion is not null) CheckPair(project.Document, companion);
        await SaveProjectAsync(id, project with { CompanionId = companionId }, cancellationToken).ConfigureAwait(false);
    }

    private static void CheckPair(SkinDocument primary, SkinDocument companion)
    {
        if (primary.Model != SkinModel.Cape || companion.Model == SkinModel.Cape)
            throw new ArgumentException("Choose a character skin for a cape preview.");
    }

    /// <summary>Opens an editor-owned snapshot for repeated render, pick and incremental stroke queries without per-pointer disk reads.</summary>
    public SkinEditingSession OpenEditor(string id) => new(this, id, Read(id));

    /// <summary>Opens a managed project file, or atomically copies an external project with its full content history into this store.</summary>
    /// <param name="input">The selected current-format .rmcs project path; external source bytes are never modified.</param>
    /// <param name="cancellationToken">Cancels before publication without changing the source or existing projects.</param>
    /// <returns>The opened session and validated display name. External copies receive an independent identity and no inherited execution receipts.</returns>
    public async Task<(SkinEditingSession Session, string Name)> OpenProjectFileAsync(string input, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var source = ResourceLease.CanonicalPath(input);
        var project = ReadProjectFile(source);
        var id = Path.GetFileNameWithoutExtension(source);
        var directory = ResourceLease.CanonicalPath(Path.Combine(paths.Root, "skins"));
        if (Guid.TryParseExact(id, "N", out _) && string.Equals(Path.GetDirectoryName(source), directory, StringComparison.OrdinalIgnoreCase)
            && string.Equals(source, ResourceLease.CanonicalPath(DocumentPath(id)), StringComparison.OrdinalIgnoreCase))
        {
            return (new(this, id, project.Document), project.Name);
        }

        id = Guid.NewGuid().ToString("N");
        project = project with { Receipts = [], ModifiedAt = DateTimeOffset.UtcNow, IsCapePreviewSkin = false };
        await SaveProjectAsync(id, project, cancellationToken).ConfigureAwait(false);
        return (new(this, id, project.Document), project.Name);
    }

    /// <summary>Lists valid projects in most-recent publication order; damaged projects are reported as errors rather than silently hidden.</summary>
    public SkinProjectInfo[] List() =>
        [.. ProjectIds().Select(id => ProjectInfo(id, ReadProject(id))).OrderByDescending(project => project.ModifiedAt)];

    /// <summary>Lists valid character works imported for shared cape previews, including previously linked cape references.</summary>
    public SkinProjectInfo[] ListCapePreviewSkins()
    {
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var characters = new List<SkinProjectInfo>();
        foreach (var id in ProjectIds())
        {
            SkinProject project;
            try
            {
                project = ReadProject(id);
            }
            catch (Exception error) when (error is IOException or InvalidDataException or System.Text.Json.JsonException or ArgumentException or UnauthorizedAccessException)
            {
                continue;
            }

            if (project.Document.Model == SkinModel.Cape)
            {
                if (project.CompanionId is { } reference)
                    references.Add(reference);
                continue;
            }

            characters.Add(ProjectInfo(id, project));
            if (project.IsCapePreviewSkin)
                references.Add(id);
        }

        return [.. characters.Where(work => references.Contains(work.DocumentId)).OrderByDescending(work => work.ModifiedAt)];
    }

    private IEnumerable<string> ProjectIds()
    {
        var directory = Path.Combine(paths.Root, "skins");
        return Directory.Exists(directory)
            ? Directory.EnumerateFiles(directory, "*" + ProjectFileExtension).Select(file => Path.GetFileNameWithoutExtension(file))
            : [];
    }

    private static SkinProjectInfo ProjectInfo(string id, SkinProject project) =>
        new(id, project.Name, project.Document.Model, project.Document.Size, project.Document.Revision, project.ModifiedAt, project.CompanionId);

    /// <summary>Copies a project to a new identity with independent pixels and a fresh history; the source is unchanged.</summary>
    public async Task<string> CloneAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        var source = Read(id);
        var copy = SkinEditor.ReplaceContent(SkinEditor.Create(source.Model, size: source.Size), source, 0, cancellationToken: cancellationToken);
        copy = copy with { Revision = 0, Undo = [], Redo = [], Transaction = new("clone", "manual", id) };
        var newId = Guid.NewGuid().ToString("N");
        await SaveProjectAsync(newId, new(copy, CheckName(name), DateTimeOffset.UtcNow, []), cancellationToken).ConfigureAwait(false);
        return newId;
    }

    /// <summary>Renames project metadata under the document lease without adding a drawing history step.</summary>
    public async Task RenameAsync(string id, string name, CancellationToken cancellationToken = default)
    {
        name = CheckName(name);
        using var lease = await ResourceLease.AcquireAsync(DocumentPath(id), cancellationToken).ConfigureAwait(false);
        var project = ReadProject(id);
        await SaveProjectAsync(id, project with { Name = name, ModifiedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Applies one optimistic drawing transaction and publishes its history atomically.</summary>
    public Task<SkinDocument> EditAsync(string id, long expectedRevision, SkinEdit[] edits, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, document => SkinEditor.Apply(document, edits, expectedRevision, cancellationToken), cancellationToken);

    /// <summary>Atomically mirrors a limb's visible base and optional outer pixels onto the opposite limb of one paint layer.</summary>
    /// <param name="id">The managed project identity.</param>
    /// <param name="expectedRevision">The observed revision, checked under the project lease.</param>
    /// <param name="part">The source arm or leg.</param>
    /// <param name="includeOuter">Whether the visible outer shell is included.</param>
    /// <param name="paintLayerId">The destination paint layer, or null for the protected background.</param>
    /// <param name="cancellationToken">Cancels before atomic publication.</param>
    /// <returns>The saved document with one shared undo step, without duplicating paint layers.</returns>
    public Task<SkinDocument> CopyPartToOppositeAsync(string id, long expectedRevision, SkinPart part,
        bool includeOuter = true, string? paintLayerId = null, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, document => SkinEditor.CopyPartToOpposite(document, part, expectedRevision, includeOuter, paintLayerId, cancellationToken), cancellationToken);

    /// <summary>Previews a complete gesture without saving pixels, history or a new revision.</summary>
    public SkinDocument PreviewEdits(string id, long expectedRevision, SkinEdit[] edits, CancellationToken cancellationToken = default) =>
        SkinEditor.Preview(Read(id), edits, expectedRevision, cancellationToken);

    /// <summary>Returns raster-accurate brush coverage for a future UV or projected native cursor.</summary>
    public SkinBrushMark[] BrushFootprint(string id, SkinEdit edit, CancellationToken cancellationToken = default) =>
        SkinEditor.BrushFootprint(Read(id), edit, cancellationToken);

    /// <summary>Imports PNG content as a single undoable replacement, including its explicit arm model and decoded dimensions.</summary>
    public async Task<SkinDocument> ImportAsync(string id, long expectedRevision, string input, SkinModel model, CancellationToken cancellationToken = default)
    {
        var content = await DecodeAsync(input, model, cancellationToken).ConfigureAwait(false);
        return await ImportContentAsync(id, expectedRevision, content, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Reads and validates a bounded PNG once, retaining original bytes independently of later file changes.</summary>
    public static async Task<SkinPngImport> PrepareImportAsync(string input, SkinModel model = SkinModel.Classic, CancellationToken cancellationToken = default) =>
        new(await ReadPngBytesAsync(input, cancellationToken).ConfigureAwait(false), model);

    /// <summary>Publishes a new project with one import transaction over a same-size blank baseline and returns its editor without rereading disk.</summary>
    public Task<SkinEditingSession> CreateImportedAsync(SkinPngImport input, SkinModel model, string name, CancellationToken cancellationToken = default) =>
        CreateImportedAsync(input, model, name, capePreviewSkin: false, cancellationToken);

    /// <summary>Imports a character PNG as an independent project available to every cape preview in this data library.</summary>
    public Task<SkinEditingSession> CreateCapePreviewSkinAsync(SkinPngImport input, SkinModel model, string name, CancellationToken cancellationToken = default)
    {
        if (model == SkinModel.Cape)
            throw new ArgumentException("A cape preview uses a character skin.", nameof(model));
        return CreateImportedAsync(input, model, name, capePreviewSkin: true, cancellationToken);
    }

    private async Task<SkinEditingSession> CreateImportedAsync(SkinPngImport input, SkinModel model, string name, bool capePreviewSkin, CancellationToken cancellationToken)
    {
        name = CheckName(name);
        var content = input.Decode(model);
        var document = SkinEditor.ReplaceContent(SkinEditor.Create(model, size: content.Size), content, 0, new("import"), cancellationToken);
        var id = Guid.NewGuid().ToString("N");
        await SaveProjectAsync(id, new(document, name, DateTimeOffset.UtcNow, [], IsCapePreviewSkin: capePreviewSkin), cancellationToken).ConfigureAwait(false);
        return new(this, id, document);
    }

    internal Task<SkinDocument> ImportContentAsync(string id, long expectedRevision, SkinDocument content, CancellationToken cancellationToken) =>
        ChangeAsync(id, document =>
        {
            if (document.Model == SkinModel.Cape != (content.Model == SkinModel.Cape)) throw new ArgumentException("Import a PNG for the current work kind, or create a separate work.");
            return SkinEditor.ReplaceContent(document, content, expectedRevision, new("import"), cancellationToken);
        }, cancellationToken);

    /// <summary>Adds an original-size transparent PNG as a new topmost paint layer in one durable undo step, preserving existing content.</summary>
    /// <param name="id">The existing project to extend.</param>
    /// <param name="expectedRevision">The observed project revision; stale input is rejected.</param>
    /// <param name="input">The PNG file to snapshot before importing.</param>
    /// <param name="name">The layer label, or null to use the input filename without its extension.</param>
    /// <param name="cancellationToken">Cancels without publishing partial project state.</param>
    public async Task<SkinDocument> ImportLayerAsync(string id, long expectedRevision, string input, string? name = null, CancellationToken cancellationToken = default) =>
        await ImportLayerAsync(id, expectedRevision, await PrepareImportAsync(input, Read(id).Model, cancellationToken).ConfigureAwait(false),
            name ?? Path.GetFileNameWithoutExtension(input), cancellationToken).ConfigureAwait(false);

    internal Task<SkinDocument> ImportLayerAsync(string id, long expectedRevision, SkinPngImport input, string name, CancellationToken cancellationToken)
    {
        name = CheckName(name);
        var pixels = input.DecodeLayer();
        return ChangeAsync(id, document =>
        {
            if (pixels.Width != document.Size || pixels.Height != document.Height)
                throw new InvalidDataException($"Layer PNG size {pixels.Width}x{pixels.Height} must match project size {document.Size}x{document.Height}.");
            var content = document with { Layers = [.. document.Layers, new(Guid.NewGuid().ToString("N"), name, pixels.Pixels)] };
            return SkinEditor.ReplaceContent(document, content, expectedRevision, new("import-layer"), cancellationToken);
        }, cancellationToken);
    }

    /// <summary>Restores one transaction while monotonically advancing the concurrency revision.</summary>
    public Task<SkinDocument> RestoreAsync(string id, long expectedRevision, bool redo = false, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, document => SkinEditor.Restore(document, expectedRevision, redo, cancellationToken), cancellationToken);

    /// <summary>Moves to a retained timeline position using one lease and one publication.</summary>
    public Task<SkinDocument> RestoreToAsync(string id, long expectedRevision, int index, CancellationToken cancellationToken = default) =>
        ChangeAsync(id, document => SkinEditor.RestoreTo(document, index, expectedRevision, cancellationToken), cancellationToken);

    /// <summary>Returns history descriptions whose indices are valid at this snapshot's revision.</summary>
    public SkinHistoryEntry[] History(string id) => SkinEditor.History(Read(id));

    /// <summary>Publishes a flattened PNG at the document's actual texture size; this does not upload an account skin.</summary>
    public Task ExportAsync(string id, string outputPath, bool overwrite = false, CancellationToken cancellationToken = default) =>
        PublishAsync(outputPath, SkinPng.Encode(Read(id)), overwrite, cancellationToken);

    /// <summary>Atomically exports only the addressed layer's original RGBA, including transparent pixels, without modifying the project.</summary>
    public Task ExportLayerAsync(string id, string layerId, string outputPath, bool overwrite = false, CancellationToken cancellationToken = default) =>
        PublishAsync(outputPath, SkinPng.EncodeLayer(Read(id), layerId), overwrite, cancellationToken);

    /// <summary>Saves a portable editable project snapshot, preserving all layers and retained undo/redo without changing the managed source.</summary>
    /// <param name="id">The managed project to preserve.</param>
    /// <param name="expectedRevision">The observed document revision; external changes are rejected rather than silently exported.</param>
    /// <param name="outputPath">The explicitly selected project file, distinct from the source.</param>
    /// <param name="overwrite">Whether the caller has authorized replacement of this exact destination.</param>
    /// <param name="cancellationToken">Cancels before atomic publication without changing the source or an existing destination.</param>
    /// <remarks>Execution receipts and separately stored AI conversations are not portable drawing content and are not included.</remarks>
    public async Task SaveProjectAsAsync(string id, long expectedRevision, string outputPath, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var source = DocumentPath(id);
        if (string.Equals(ResourceLease.CanonicalPath(source), ResourceLease.CanonicalPath(outputPath), StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Choose a destination different from the managed source project.", nameof(outputPath));
        using var lease = await ResourceLease.AcquireAsync(source, cancellationToken).ConfigureAwait(false);
        var project = ReadProject(id);
        if (project.Document.Revision != expectedRevision)
            throw new InvalidOperationException("The skin project changed. Refresh before saving a copy.");
        var bytes = SkinProjectFile.Encode(project with { Receipts = [], IsCapePreviewSkin = false }, cancellationToken);
        await PublishAsync(outputPath, bytes, overwrite, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Publishes a model preview from a durable snapshot without changing editor state.</summary>
    public Task PreviewAsync(string id, string outputPath, SkinViewOptions? options = null, bool overwrite = false, bool solo = false, CancellationToken cancellationToken = default) =>
        PublishAsync(outputPath, SkinPreview.Render(Read(id), options, solo ? null : ReadCompanion(id), cancellationToken), overwrite, cancellationToken);

    /// <summary>Publishes an uncommitted gesture preview without changing the project store.</summary>
    public static Task PublishPreviewAsync(SkinDocument preview, string output, bool overwrite = false, CancellationToken cancellationToken = default) =>
        PublishAsync(output, SkinPreview.Render(preview, cancellationToken: cancellationToken), overwrite, cancellationToken);

    /// <summary>Returns document metadata and geometry without transmitting full texture/history buffers.</summary>
    public SkinSummary Describe(string id, SkinDocument? snapshot = null)
    {
        var project = ReadProject(id);
        var document = snapshot ?? project.Document;
        return new(id, document.Revision, document.SchemaVersion, document.Model, document.Size, document.Height,
            document.Undo.Count > 0, document.Redo.Count > 0, SkinLayers.Describe(document), SkinLayout.Faces(document.Model, document.Size), project.Name, project.ModifiedAt, project.CompanionId);
    }

    private async Task<SkinDocument> ChangeAsync(string id, Func<SkinDocument, SkinDocument> change, CancellationToken cancellationToken)
    {
        using var lease = await ResourceLease.AcquireAsync(DocumentPath(id), cancellationToken).ConfigureAwait(false);
        var project = ReadProject(id);
        var document = change(project.Document);
        if (!ReferenceEquals(document, project.Document))
            await SaveProjectAsync(id, project with { Document = document, ModifiedAt = DateTimeOffset.UtcNow }, cancellationToken).ConfigureAwait(false);
        return document;
    }

    internal SkinProject ReadProject(string id) => ReadProjectFile(DocumentPath(id));

    private static SkinProject ReadProjectFile(string path)
    {
        var project = SkinProjectFile.Read(path);
        project.Document.Validate();
        CheckName(project.Name);
        if (project.CompanionId is not null) CheckId(project.CompanionId);
        if (project.Receipts is null)
            throw new InvalidDataException("The project transaction receipts are missing.");
        return project;
    }

    internal Task SaveProjectAsync(string id, SkinProject project, CancellationToken cancellationToken) =>
        JsonFiles.WriteBytesAsync(DocumentPath(id), SkinProjectFile.Encode(project, cancellationToken), cancellationToken);

    internal string DocumentPath(string id) => Path.Combine(paths.Root, "skins", CheckId(id) + ProjectFileExtension);

    internal static string CheckId(string id)
    {
        if (!Guid.TryParseExact(id, "N", out _))
            throw new ArgumentException("Use an existing skin resource identifier.", nameof(id));
        return id;
    }

    private static string CheckName(string name) => !string.IsNullOrWhiteSpace(name) && name.Length <= 200
        ? name.Trim() : throw new ArgumentException("Project name must contain 1 through 200 characters.", nameof(name));

    private static async Task<SkinDocument> DecodeAsync(string input, SkinModel model, CancellationToken cancellationToken)
    {
        return SkinPng.Decode(await ReadPngBytesAsync(input, cancellationToken).ConfigureAwait(false), model);
    }

    private static async Task<byte[]> ReadPngBytesAsync(string input, CancellationToken cancellationToken)
    {
        using var stream = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.Asynchronous);
        if (stream.Length is < 33 or > 4194304)
            throw new InvalidDataException("Choose a PNG skin no larger than 4 MiB.");
        var bytes = new byte[checked((int)stream.Length)];
        await stream.ReadExactlyAsync(bytes, cancellationToken).ConfigureAwait(false);
        return bytes;
    }

    internal static async Task PublishAsync(string outputPath, byte[] bytes, bool overwrite, CancellationToken cancellationToken)
    {
        var fullPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        var temporary = fullPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, bytes, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (overwrite && File.Exists(fullPath))
                File.Replace(temporary, fullPath, null);
            else
                File.Move(temporary, fullPath);
        }
        finally
        {
            if (File.Exists(temporary))
                File.Delete(temporary);
        }
    }

}