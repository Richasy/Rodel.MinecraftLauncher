// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Cli;

internal static class SkinToolsCatalog
{
    internal const string Instructions = """
        You draw editable Minecraft skins by calling shared editing tools, not by generating PNG bytes.
        Use skin_create to start or import. Inspect the model and face rectangles; read specific faces to sample colors.
        skin_templates includes five character templates and one plain cape (template=cape, model=Cape).
        Cape uses a separate 64x32 or 128x64 document (size=64 or 128), with Cape/Base surfaces and alpha support.
        Choose model=Cape explicitly when importing cape PNG; legacy 64x32 character PNG remains Classic.
        skin_outfit sets companionId to an opposite-kind project or clear=true to remove it, with expectedRevision.
        Pairing changes preview metadata, never artwork/history. skin_preview includes the saved pairing unless solo=true.
        Paired works remain separate; editing or exporting one never changes or flattens its companion.
        Coordinates are face-local, top-left origin. Right/left refer to the character, not the viewer.
        Colors are #RRGGBB or #RRGGBBAA. Character background Base stays opaque; cape background allows alpha.
        Paint layers are separate from Minecraft Base/Outer surfaces. Use tool=Layer and layerChange to Add/Set/Remove/Reorder/Duplicate/MergeDown.
        inspect returns paintLayers. Drawing without paintLayerId edits background; a named layer may be transparent even on Base.
        Layer changes and pixels share atomic undo/redo. Locked layers reject painting/removal/merging.
        Each skin_edit applies 1-512 typed drawing actions atomically as one undo step.
        Use Pencil/Eraser/Stroke/Line with square/circle brushShape, brushSize, opacity, softness and lockAlpha.
        Stroke points are pixel-center x/y positions, optionally with their own surface. wrapFaces transports brush footprints across the same cuboid.
        With wrapFaces, adjacent-face stroke segments are unfolded; different parts/layers or nonadjacent faces break the segment.
        mirror=Body paints bilateral character symmetry; Part reflects inside the current cuboid. Repeated samples in one action do not accumulate opacity.
        Fill supports Connected/Matching/Face/Part/Layer, tolerance and sampleMerged. A selection restricts edits to its addressed face.
        Gradient uses color/secondaryColor and x/y to x2/y2, with Linear/Radial gradientKind. AdjustColor uses hue degrees and saturation/brightness/contrast deltas.
        Use skin_select for rectangle/ellipse/polygon or matching/connected masks, optionally inverted. Reuse the returned selection in drawing actions.
        Use skin_capture for clipboard pixels and footprint, then Paste with clipboard. Copy/Move use source/sourceSelection.
        Clipboard transforms support quarterTurns, flipX/flipY and nearest-neighbor destinationWidth/destinationHeight.
        Cut/Move that clear character background Base require an opaque clearColor. Moves reject clipped destinations instead of discarding data.
        Cut, Move and flips require opacity=1 and lockAlpha=false; Copy/Paste support partial-opacity or alpha-locked painting.
        Shapes use inclusive x2/y2. Pixel replacement and copying default to Replace blending; brushes default to source-over Normal.
        skin_pick accepts a model-space ray and returns surface/pixel/stroke coordinates. Choose layer=Outer and skipTransparent=false to paint empty outer surfaces.
        Always supply the last observed expectedRevision. A stale transaction fails without changing the document.
        Inspect on conflicts; do not blindly replay a successful or interrupted drawing transaction.
        Use skin_preview after meaningful changes to inspect actual 3D geometry; PNG previews are returned as MCP images.
        Preview view controls yaw/pitch/size, baseVisible/outerVisible and parts (null=all, []=none).
        Preview files are PNG snapshots, not client editors. Manual editing UI belongs to the native Sprout app.
        Use skin_import with asLayer=true to add a matching transparent PNG as a new paint layer; never replace a skin to add a shawl.
        Use skin_export for the exact current-work PNG, or paintLayerId for one layer's original RGBA. Cape exports retain rectangular dimensions.
        Export does not change a Minecraft account or apply a skin/cape in game. File paths require explicit authority.
        Model requests, design reasoning and the AI agent loop belong to your external AI host, not this server.
        Use skin_history/skin_restore for indexed history; revision is concurrency state, not a timeline position.
        skin_propose uses a host-created scope. By default it freezes an uncommitted candidate for host review.
        If that host grant explicitly has directEditing=true, skin_propose commits immediately and returns an acceptance receipt; no separate approval is needed.
        A scope-bound catalog omits direct-edit commands, acceptance, grant creation, and arbitrary file paths; it does not change the grant's commit mode.
        Use skin_receipt to resolve interrupted acceptance; an accepted operation is never replayed. Stale candidates must be regenerated.
        128 textures are editable HD assets; standard Java account upload uses 64x64. Export does not upload.
        No launcher management or arbitrary execute tool is available in this profile.
        """;

    internal const string ScopedInstructions = """
        This host is bound to one editor grant. Use only the advertised tools to observe and edit that document within its actual grant.
        Inspect actual model, texture size, revision and face rectangles first. Coordinates are face-local texels, with top-left origin.
        Classic/Slim arm geometry is independent of 64/128 texture size. 128 is HD, not a standard Java account-upload skin.
        Cape is a separate rectangular model with Cape/Base faces; it is not a character paint layer. Paired references do not expand this grant.
        Colors are #RRGGBB or #RRGGBBAA. Paint layers differ from Minecraft Base/Outer surfaces; character background Base stays opaque, while cape background allows alpha.
        Use skin_propose with the bound scopeId and last observed expectedRevision. Actions share the real drawing engine and host-enforced layer/surface/mask limits.
        Pencil/Eraser accept sampled points; Stroke paints a continuous path. Wrap and mirror remain subject to the actual authorized effects.
        By default a proposal is frozen and uncommitted for host review. A host grant with directEditing=true instead applies skin_propose immediately and returns an acceptance receipt.
        Inspect skin_proposal_show or the actual image from skin_proposal_preview. Only pending candidates need host acceptance or skin_proposal_reject; never confuse them with accepted receipts.
        If manual edits make a candidate stale, reread and generate against the latest revision; never alter a candidate's base revision or blindly replay effects.
        skin_receipt observes a host acceptance result. History entries describe states; indices are not concurrency revisions.
        No direct edits, grant creation, acceptance, arbitrary output paths, cross-project results or launcher-management tools are exposed here.
        """;

    internal static IReadOnlyList<LauncherToolDefinition> Tools { get; } = Build();

    internal static IReadOnlyList<LauncherToolDefinition> ScopedTools { get; } = [.. Tools.Where(tool => tool.Name is
        "skin_inspect" or "skin_read_face" or "skin_pick" or "skin_select" or "skin_capture" or "skin_history" or "skin_footprint"
        or "skin_templates" or "skin_propose" or "skin_proposal_show" or "skin_proposal_reject" or "skin_proposal_preview" or "skin_receipt")];

    private static IReadOnlyList<LauncherToolDefinition> Build() => [.. ToolCatalog.Entries.Values
        .Where(entry => entry.Declaration.Name.StartsWith("skin_", StringComparison.Ordinal) || entry.Declaration.Name == "result_read")
        .Select(LauncherToolsCatalog.CoreDefinition)];
}