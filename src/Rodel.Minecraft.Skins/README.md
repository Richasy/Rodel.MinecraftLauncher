# Rodel.Minecraft.Skins

A UI-independent .NET 11 library for editing Minecraft Java Edition skins. It provides pixel editing, paint layers, selections, transactional history, UV geometry, PNG import/export, and CPU-rendered 3D previews.

The library supports 64 x 64 and HD 128 x 128 Classic and Slim textures, including import of legacy 64 x 32 textures. It is compatible with NativeAOT and can be used independently of the Rodel launcher.

## Features

- **Brushes and strokes:** square and circular brushes, size, opacity, soft edges, alpha locking, source-over and replacement blending, and connected stroke samples.
- **Surface-aware editing:** body and part mirroring, brushes across cube edges, unfolded strokes across adjacent faces, and connected fills across faces of the same body part.
- **Shapes and fills:** lines, outlined or filled rectangles and ellipses, connected and color-matched fills, and fills scoped to a face, body part, or Minecraft skin layer.
- **Color tools:** exact RGBA sampling, color replacement, RGB shading, HSV adjustments, contrast, and linear or radial gradients with premultiplied-alpha interpolation.
- **Selections and transfers:** rectangular, elliptical, polygonal, color-matched, and connected masks; inversion, capture, copy, cut, move, paste, flips, quarter-turn rotation, and nearest-neighbor scaling.
- **Paint layers:** naming, visibility, opacity, locking, duplication, reordering, deletion, and merge-down.
- **History and concurrency:** atomic edit batches, revision checks, cancellation, and undo/redo of both pixels and layer settings.
- **Geometry and output:** UV layout queries, ray picking, model quads, composited PNG export, and configurable 3D PNG previews.

## Quick start

Reference `Rodel.Minecraft.Skins.csproj` from a .NET 11 application:

```csharp
using Rodel.Minecraft.Skins;

var document = SkinEditor.Create(SkinModel.Slim, "#263449");
document = SkinEditor.Apply(document,
[
    new SkinEdit
    {
        Tool = SkinTool.Layer,
        LayerChange = new SkinLayerChange
        {
            Operation = SkinLayerOperation.Add,
            Id = "details"
        }
    },
    new SkinEdit
    {
        Tool = SkinTool.Stroke,
        PaintLayerId = "details",
        Surface = new SkinSurface
        {
            Part = SkinPart.Head,
            Layer = SkinLayer.Base,
            Face = SkinFace.Front
        },
        Points = [new(1, 2), new(6, 5)],
        Color = "#FFFFFF",
        BrushShape = SkinBrushShape.Circle,
        BrushSize = 3,
        Opacity = 0.5,
        WrapFaces = true,
        Mirror = SkinMirrorMode.Body
    }
], expectedRevision: 0);

byte[] texture = SkinPng.Encode(document);
byte[] preview = SkinPreview.Render(document);
```

This batch creates a paint layer and draws a stroke as a single undoable transaction. The caller decides how to display or persist the resulting document and image bytes.

## Editing model

### Documents and transactions

`SkinDocument` contains the skin model, background texture, paint layers, revision, and history. `SkinEdit` describes an action; `SkinEditor.Apply` executes a batch of 1 to 512 actions against a private copy of the document.

A successful batch that changes content advances the revision once, records one undo step, and clears redo history. A batch that makes no changes returns the original document. Errors and cancellation do not modify the input document or publish partial results.

Pass the last observed revision as `expectedRevision` to reject stale edits. `SkinEditor.Restore` performs undo or redo while advancing the revision. History retains up to 32 transactions.

`SkinDocument.Size` is 64 or 128. Face-local coordinates and brush sizes are actual texels; pass the size to `SkinLayout.Face/Faces` and `SkinGeometry.Quads`. World geometry remains the same at both resolutions. `SkinViewOptions.Size` is the output preview size, independent of texture size. HD editing/export does not imply standard Java account-upload compatibility.

`SkinEditor.Preview` uses the same raster engine on an owned content copy, keeping the observed revision/history. Recompute the cumulative gesture against its original snapshot and submit once on release; discard the preview to cancel. `Pencil` and `Eraser` accept `Points` for continuous paths, as does `Stroke`; coverage does not accumulate inside one action. Incremental stroke caching is not required by this API and has not been performance-benchmarked.

`SkinEditor.BrushFootprint` returns geometric texel coverage before blending, including size, shape, selection, mirrors and wrapped edges. Hosts project those marks using shared quads instead of duplicating raster rules.

`SkinEditor.ReplaceContent` replaces model, dimensions and layers as one undo step. `History` exposes retained positions and semantic transaction descriptions; pair an index with the observed revision in `RestoreTo`. Navigation preserves future states, no-op edits preserve redo, and a real pixel/layer edit forks the history. A history index is never a concurrency revision.

`SkinTemplates.Ids`, `SuggestedModel` and `Create` provide the five original `classic/alex/casual/base/blank` seeds in either arm model and resolution. The packaged PNG seeds are original assets extracted from the accepted prototype, not Mojang textures; runtime never reads `design`. Hosts localize template labels and treat Alex's Slim model as a suggestion. Each creation owns independent buffers.

### Paint layers and skin surfaces

Paint layers are composited RGBA textures, distinct from Minecraft's Base and Outer geometry layers. The required `background` texture keeps Base surfaces opaque; additional paint layers may contain transparency anywhere. Clearing a background Base surface requires an opaque `clearColor`.

`SkinLayers` exposes layer operations, descriptions, and composited or individual-layer pixels. The protected background can be renamed via a Set/Name action but retains its position and opaque Base constraint. PNG export, previews, merged sampling, and transparency-aware picking share the same visible composite. Merging layers bakes their visibility and opacity into the result; undo can restore the original layers.

Colors use 8-bit RGBA channels. Alpha locking skips transparent pixels and preserves existing alpha. Within one drawing action, repeated brush coverage uses the maximum coverage rather than accumulating opacity; separate actions can accumulate color.

### Selections and pixel transfers

Selections are scoped to one model face. `SkinSelections.Create` builds geometric or color-based masks, and `SkinEditor.Capture` returns a masked `SkinPixelBlock` for reuse in another action or document.

Source and destination selections are independent. Copy and paste allow destination clipping; moves reject clipping that would lose source pixels. Cut, move, and flip operations require full opacity and alpha locking disabled so that clearing the source cannot silently discard pixels.

`SkinEditor.CopyPartToOpposite` mirrors all six faces of one arm or leg into its counterpart on a single destination paint layer. It samples the visible composite, including visible paint-layer opacity, and replaces destination RGBA rather than blending it again. Base surfaces are always included; `includeOuter` controls the outer shell. Other layers and the document transparency mask remain unchanged. The entire copy uses one revision-checked undo transaction; protected background Base pixels still cannot receive transparency.

`WrapFaces` extends supported operations across cube edges within a body part. Strokes break between nonadjacent faces, different body parts, or different Minecraft geometry layers. An explicit selection confines editing to its face.

With `Mirror = Body`, optional `MirrorParts` lists the visible partners eligible for symmetry. Each addressed arm or leg mirrors to its counterpart only when that counterpart is listed; otherwise it reflects through its cuboid's opposite face: front/back, left/right, or top/bottom. Reflection follows the addressed face normal; upright faces reverse local X, while top/bottom reverse local Y to preserve physical position. It does not duplicate the cursor across the same face. Heads and torsos retain bilateral symmetry. This also applies per sample in a multi-part stroke and to brush/fill footprints. Omit the list to retain whole-body pairing, or use `Mirror = Part` for the original unconditional local bilateral symmetry.

## Geometry, previews, and serialization

- `SkinLayout.Face` and `Faces` expose the standard UV layout.
- `SkinGeometry.Pick` accepts a ray in model-pixel coordinates and returns the nearest matching surface, pixel indices, continuous stroke coordinates, and normal. To target an empty Outer surface, use `SkinPickRequest.Layer = SkinLayer.Outer` with `SkipTransparent = false`.
- `SkinGeometry.Quads` exposes the same model geometry used by previews.
- `SkinViewOptions` controls preview dimensions, camera yaw/pitch, and visible parts and geometry layers. A null `Parts` value includes all parts; an empty array includes none.
- `SkinEditor.Sample` reads an exact color from the visible composite or an explicitly selected paint layer.
- `SkinJsonContext` provides source-generated JSON metadata for documents and operation models.

Document format 3 preserves model, texture size, background name, transaction descriptions, paint layers and complete undo/redo state. PNG exports contain the flattened texture, not editable layers or history. Legacy development document formats 1 and 2 are not read or implicitly migrated; standard PNG files can be imported into new documents.

## Integration

The library has no dependency on launcher services, UI frameworks, account services, or AI SDKs. It does not manage application directories, file leases, system clipboard access, pointer input, or persistence. Hosts own those concerns and can pass cancellation tokens to editing operations.

PNG encoding and decoding use the managed [StbImageSharp](https://github.com/StbSharp/StbImageSharp) and [StbImageWriteSharp](https://github.com/StbSharp/StbImageWriteSharp) packages. Preview rendering runs on the CPU and requires no browser, WebView, or online service.

The API targets standard Minecraft skin textures rather than arbitrary meshes, custom UV layouts, arbitrary HD sizes, skeletal animation, or a general-purpose modeling environment.

## Project structure

All public types use the `Rodel.Minecraft.Skins` namespace.

| Location | Responsibility |
| --- | --- |
| `SkinEditor.cs` | Public transaction, history, sampling, and capture entry points |
| `Models/` | Documents, snapshots, requests, results, and enums |
| `Editing/` | Drawing execution, selections, layer changes, and pixel-transfer planning |
| `Geometry/` | UV layout, face frames, ray picking, and surface relationships |
| `Imaging/` | Color calculations, shared read-only compositing, PNG codecs, and preview rendering |
| `Serialization/` | Module-wide JSON source-generation context and enum converters |

The dedicated [test project](../../test/Rodel.Minecraft.Skins.Tests/README.md) covers library behavior independently of the launcher. See the [test guide](../../test/README.md) for execution instructions.

## References

The following projects informed tool behavior and feature selection. They are not runtime dependencies, and their GPL source code is not copied or ported into this library.

| Project | Source revision | Reference areas |
| --- | --- | --- |
| Blockbench | [`ec0b2827104d0799c153bfb5f867579c0c44652f`](https://github.com/JannisX11/blockbench/blob/ec0b2827104d0799c153bfb5f867579c0c44652f/js/texturing/painter.js) | Brush softness and opacity, alpha locking, mirroring, gradients, fill modes, and continuous strokes |
| NeedCoolerShoes | [`e603440257e94dc7838bcdf7265716a06914d9a0`](https://github.com/NeedCoolerShoes/editor/tree/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui) | [Brush configuration](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/config/tabs/tools/configs/pen_tool_config.js), [paint layers](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/layers/layer_list.js), and [model visibility controls](https://github.com/NeedCoolerShoes/editor/blob/e603440257e94dc7838bcdf7265716a06914d9a0/src/ui/tools/editor_toggles.js) |
