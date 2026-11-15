// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.App.Features.Ai;
using System.Globalization;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Explains recorded skin operations in player terms while keeping requested actions distinct from their completion status.</summary>
internal static class SkinAiToolText
{
    internal static string Name(string name) => name switch
    {
        "skin_propose" => Resources.SkinAiToolEdit,
        "skin_proposal_preview" => Resources.SkinAiToolPreview,
        "skin_inspect" or "skin_read_face" or "skin_capture" => Resources.SkinAiToolRead,
        "skin_select" or "skin_pick" or "skin_footprint" => Resources.SkinAiToolLocate,
        "skin_history" or "skin_proposal_show" or "skin_receipt" => Resources.SkinAiToolHistory,
        "skill_read" or "skill_reference_read" => Resources.SkinAiToolInstructions,
        _ => name,
    };

    internal static string[] Describe(AiTranscriptPart part, SkinAiToolDetails details, Func<string?, string> layerName)
    {
        if (details.Error is { } error)
            return [string.Format(Resources.SkinAiToolDetailsError, error)];
        if (details.Edits.Length > 0)
        {
            return [.. details.Edits.Select(edit => part.Name == "skin_footprint" ? string.Format(Resources.SkinAiFootprintDetails, Describe(edit, layerName)) : Describe(edit, layerName))];
        }

        if (details.Surface is { } surface)
        {
            return [string.Format(Resources.SkinAiReadSurface, Surface(surface), details.PaintLayerId is null ? Resources.SkinAiComposite : layerName(details.PaintLayerId))];
        }

        return part.Name switch
        {
            "skin_inspect" => [Resources.SkinAiInspectDetails],
            "skin_proposal_preview" => [string.Format(Resources.SkinAiPreviewDetails, details.View?.Parts is { } parts ? string.Join(" / ", parts.Select(Part)) : Resources.SkinAiWholeSkin)],
            "skin_history" => [Resources.SkinAiReadHistoryDetails],
            "skin_receipt" or "skin_proposal_show" => [Resources.SkinAiReadReceiptDetails],
            "skill_read" or "skill_reference_read" => [Resources.SkinAiReadSkillDetails],
            _ => [],
        };
    }

    private static string Describe(SkinEdit edit, Func<string?, string> layerName)
    {
        if (edit.Points is { Length: > 0 } sampled && sampled.Any(point => point.Surface is { } surface && surface != edit.Surface))
        {
            return string.Join(Environment.NewLine, sampled.GroupBy(point => point.Surface ?? edit.Surface).Select(group => Describe(edit with
            {
                Surface = group.Key,
                Points = [.. group.Select(point => point with { Surface = null })],
            }, layerName)));
        }

        if (edit.Tool == SkinTool.Layer && edit.LayerChange is { } layer)
        {
            var name = layer.Name ?? layerName(layer.Id);
            return layer.Operation switch
            {
                SkinLayerOperation.Add => string.Format(Resources.SkinAiLayerAddDetails, name),
                SkinLayerOperation.Duplicate => string.Format(Resources.SkinAiLayerCopyDetails, layerName(layer.SourceId), name),
                SkinLayerOperation.Remove => string.Format(Resources.SkinAiLayerRemoveDetails, name),
                SkinLayerOperation.MergeDown => string.Format(Resources.SkinAiLayerMergeDetails, name),
                SkinLayerOperation.Reorder => string.Format(Resources.SkinAiLayerOrderDetails, name, layer.Index),
                _ when layer.Visible == false => string.Format(Resources.SkinAiLayerHideDetails, name),
                _ when layer.Visible == true => string.Format(Resources.SkinAiLayerShowDetails, name),
                _ when layer.Locked == true => string.Format(Resources.SkinAiLayerLockDetails, name),
                _ when layer.Locked == false => string.Format(Resources.SkinAiLayerUnlockDetails, name),
                _ when layer.Opacity is { } opacity => string.Format(Resources.SkinAiLayerOpacityDetails, name, opacity),
                _ => string.Format(Resources.SkinAiLayerSetDetails, name),
            };
        }

        var target = edit.Surface is { } surface ? Surface(surface) : Resources.SkinAiAtlas;
        var area = edit.Tool == SkinTool.Fill
            ? edit.FillMode switch
            {
                SkinFillMode.Face => Resources.SkinAiAreaFace,
                SkinFillMode.Part => Resources.SkinAiAreaPart,
                SkinFillMode.Layer => Resources.SkinAiAreaLayer,
                _ => string.Format(Resources.SkinAiAreaFrom, Point(edit.X, edit.Y)),
            }
            : edit.Points is { Length: > 0 } points
            ? string.Format(Resources.SkinAiAreaStroke, points.Length, Point(points[0].X, points[0].Y), Point(points[^1].X, points[^1].Y))
            : edit.AtlasTransfer is { } transfer
            ? Point(transfer.X, transfer.Y)
            : edit.X2 is not null || edit.Y2 is not null ? string.Format(Resources.SkinAiAreaRange, Point(edit.X, edit.Y), Point(edit.X2 ?? edit.X, edit.Y2 ?? edit.Y)) : Point(edit.X, edit.Y);
        if (edit.Selection is not null || edit.PixelMask is not null)
            area = string.Format(Resources.SkinAiAreaMasked, area);
        var action = edit.Tool switch
        {
            SkinTool.Pencil => Resources.SkinBrush,
            SkinTool.Eraser => Resources.SkinEraser,
            SkinTool.Stroke => Resources.SkinHistoryStroke,
            SkinTool.Fill => Resources.SkinFill,
            SkinTool.Rectangle => edit.Filled ? Resources.SkinAiRectangleFill : Resources.SkinAiRectangleOutline,
            SkinTool.Ellipse => edit.Filled ? Resources.SkinAiEllipseFill : Resources.SkinAiEllipseOutline,
            SkinTool.Line => Resources.SkinAiLine,
            SkinTool.Gradient => Resources.SkinAiGradient,
            SkinTool.Replace => Resources.SkinAiReplaceColor,
            SkinTool.Shade or SkinTool.AdjustColor => Resources.SkinAiAdjustColor,
            SkinTool.Copy => Resources.SkinAiCopy,
            SkinTool.Move => Resources.SkinHistoryMove,
            SkinTool.Cut => Resources.SkinCutSelection,
            SkinTool.Paste => Resources.SkinPasteSelection,
            _ => edit.Tool.ToString(),
        };
        if (edit.Mirror != SkinMirrorMode.None)
            action = string.Format(Resources.SkinAiMirroredAction, action);
        var address = string.Format(Resources.SkinAiEditAddress, target, layerName(edit.PaintLayerId));
        var description = edit.Color is { } color ? string.Format(
            Resources.SkinAiColoredEditDetails,
            address,
            action,
            area,
            Technical(color)) : string.Format(Resources.SkinAiEditDetails, address, action, area);
        return edit.Source is { } source ? string.Format(Resources.SkinAiEditSource, description, Surface(source)) : description;
    }

    internal static string Surface(SkinSurface surface) => string.Format(Resources.SkinAiSurface, Part(surface.Part), surface.Layer == SkinLayer.Outer ? Resources.SkinOuterSurface : Resources.SkinBaseSurface, surface.Face switch
    {
        SkinFace.Front => Resources.SkinAiFaceFront,
        SkinFace.Back => Resources.SkinAiFaceBack,
        SkinFace.Right => Resources.SkinAiFaceRight,
        SkinFace.Left => Resources.SkinAiFaceLeft,
        SkinFace.Top => Resources.SkinAiFaceTop,
        _ => Resources.SkinAiFaceBottom,
    });

    private static string Part(SkinPart part) => part switch
    {
        SkinPart.Cape => Resources.SkinCape,
        SkinPart.Head => Resources.SkinHead,
        SkinPart.Body => Resources.SkinTorso,
        SkinPart.RightArm => Resources.SkinRightArm,
        SkinPart.LeftArm => Resources.SkinLeftArm,
        SkinPart.RightLeg => Resources.SkinRightLeg,
        _ => Resources.SkinLeftLeg,
    };

    private static string Point(double x, double y) => Technical(string.Create(CultureInfo.InvariantCulture, $"({x:0.##}, {y:0.##})"));

    private static string Technical(string value) => "\u2068" + value + "\u2069";
}