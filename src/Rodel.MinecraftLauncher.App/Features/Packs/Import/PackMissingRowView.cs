// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

internal sealed record PackMissingEntry(
    int Index,
    string Name,
    string Location,
    string Evidence,
    bool RightToLeft)
{
    internal static PackMissingEntry From(PackMissingFile item, int index, bool rightToLeft)
    {
        var path = item.Path;
        if (path is not null)
        {
            return new(
                index,
                System.IO.Path.GetFileName(path),
                path.Split('/')[0],
                path,
                rightToLeft);
        }

        return new(
            index,
            item.ManualFileName ?? item.ManualKey ?? string.Format(Resources.PackMissingIdentity, item.ProjectId?.ToString() ?? "?", item.FileId?.ToString() ?? "?"),
            item.ManualKey is { } key ? "project:file " + key : string.Empty,
            JsonSerializer.Serialize(item, PackJsonContext.Default.PackMissingFile),
            rightToLeft);
    }
}

[View]
internal readonly partial record struct PackMissingRowView(LauncherAppearance Theme, PackMissingEntry Entry)
{
    /// <summary>Shows one missing-file card with its category or offline mapping key in a trailing tag.</summary>
    public Ui Body => Overlay([
        new Controls.LauncherMaterialSurface(Theme.Materials.Acrylic).Margin(1f),
        Border(Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Flow(FlowDirection.LeftToRight, Text(Entry.Name)
                        .FontSize(13f).Foreground(Theme.Ink)
                        .NoWrap()
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Align(Entry.RightToLeft ? TextAlignment.Trailing : TextAlignment.Leading)
                        .Automation(new()
                        {
                            Name = Entry.Name,
                            HelpText = Entry.Evidence,
                            AutomationId = "pack.import.missing." + Entry.Index
                        }))
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Border(Flow(FlowDirection.LeftToRight, Text(Entry.Location)
                            .FontSize(12f).Foreground(Theme.Muted)
                            .NoWrap()
                            .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()))
                    .Padding(8f, 4f)
                    .Background(Theme.HighContrast ? Theme.ControlBackground : Theme.Ink.MultiplyAlpha(.08f))
                    .BorderBrush(Theme.Edge)
                    .BorderThickness(Theme.HighContrast ? 1f : 0f)
                    .MaxWidth(180f)
                    .Visible(Entry.Location.Length > 0)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f))
        .Padding(12f)
        .BorderBrush(Theme.Edge)
        .BorderThickness(1f),
    ]).Margin(new Thickness(0f, 0f, 0f, 8f));
}

internal static class PackImportText
{
    internal static string Acquisition(PackState? result)
    {
        var acquisition = result?.Pack?.Acquisition;
        return acquisition is null ? string.Empty : string.Format(
            Resources.PackAcquisitionSummary,
            acquisition.LocalDeclaredFiles,
            acquisition.DownloadedDeclaredFiles,
            acquisition.ArchiveFiles,
            acquisition.MissingDeclaredFiles);
    }

    internal static string Missing(PackState? result) => string.Join(Environment.NewLine, (result?.Missing ?? []).Select(item => item.ManualKey is { } key ? string.Format(Resources.PackMissingManualFile, Isolate(item.ManualFileName ?? string.Empty), Isolate(key)) : string.Format(Resources.PackMissingFile, item.Path ?? string.Format(Resources.PackMissingIdentity, item.ProjectId?.ToString() ?? "?", item.FileId?.ToString() ?? "?"))));

    private static string Isolate(string value) => "\u2066" + value + "\u2069";
}