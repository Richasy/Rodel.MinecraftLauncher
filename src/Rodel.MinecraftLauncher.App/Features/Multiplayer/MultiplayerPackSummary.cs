// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Packs;
using Rodel.MinecraftLauncher.App.Imaging;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using System.Text.Json.Nodes;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Displays declared pack metadata and the size captured during inspection without reading files while rendering.</summary>
internal sealed partial class MultiplayerPackSummary : Control
{
    private readonly MultiplayerController _controller;
    private partial int Revision { get; set; }
    private PackImportState State { get { _ = Revision; return _controller.Pack.State; } }
    private PackInspection? Info => State.Inspection ?? State.Result?.Pack;
    private string FileName => System.IO.Path.GetFileName(State.Source);
    private string Title => Scalar(Info?.Name) is { Length: > 0 } name ? name : System.IO.Path.GetFileNameWithoutExtension(FileName);
    private string Version => Scalar(Info?.VersionId);
    private string Description => Scalar(Info?.Manifest["summary"]) is { Length: > 0 } summary ? summary : Scalar(Info?.Manifest["description"]);
    private string ArchiveSize => State.ArchiveBytes is { } bytes ? AppImageCache.FormatSize(bytes) : string.Empty;
    private string Format => System.IO.Path.GetExtension(FileName).TrimStart('.').ToUpperInvariant();
    private string Files => Info?.Manifest["files"] is JsonArray files
        ? string.Format(Resources.MultiplayerPackFileCounts, files.Count, Info.OptionalFiles.Count)
        : string.Empty;
    private string Author => Scalar(Info?.Manifest["author"]) is { Length: > 0 } author ? author
        : Info?.Manifest["authors"] is JsonArray authors
        ? string.Join(" · ", authors.Select(item => item is JsonObject record ? Scalar(record["name"]) : Scalar(item)).Where(value => value.Length > 0))
        : string.Empty;
    private string Loader
    {
        get
        {
            if (Info is not { } info) return string.Empty;
            var loaders = new List<string>();
            foreach (var (key, name) in LoaderNames)
            {
                if (info.Dependencies.TryGetValue(key, out var value) && Scalar(value) is { Length: > 0 } version)
                {
                    loaders.Add(name + " " + version);
                }
            }

            return loaders.Count > 0 ? string.Join(" · ", loaders)
                : info.Dependencies.Count == 1 && info.Dependencies.ContainsKey("minecraft") ? Resources.Vanilla : Resources.DiagnosticsUnknownStatus;
        }
    }
    private static readonly (string Key, string Name)[] LoaderNames = [("fabric-loader", "Fabric"), ("quilt-loader", "Quilt"), ("forge", "Forge"), ("neoforge", "NeoForge")];

    internal MultiplayerPackSummary(MultiplayerController controller)
    {
        _controller = controller;
        controller.Changed += OnChanged;
    }

    private void OnChanged() => Revision++;
    private static string Scalar(JsonNode? value) => value is JsonValue ? value.ToString() : string.Empty;

    private Ui Body => Border(Column([
        Grid([GridLength.Star(), GridLength.Auto], [GridLength.Auto], [
            Column([
                Text(Title).FontSize(18f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap()
                    .Automation(new() { Name = Title, AutomationId = "multiplayer.pack.title" }),
                Text(FileName).FontSize(11f).Foreground(LauncherTheme.Muted).Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                    .Automation(new() { Name = FileName, AutomationId = "multiplayer.pack.file" }),
            ]).Spacing(6f).Cell(0, 0),
            Text(Format).FontSize(11f).Foreground(LauncherTheme.Muted).VAlign(VerticalAlignment.Center).Cell(1, 0),
        ]).ColumnSpacing(12f),
        Text(Description).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().MaxLines(3).Trim(TextTrimming.CharacterEllipsis)
            .Visible(Description.Length > 0),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto, GridLength.Auto, GridLength.Auto], [
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.MultiplayerPackVersion, Version, "version").Visible(Version.Length > 0).Cell(0, 0),
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.CreateVersion, Info?.MinecraftVersion ?? string.Empty, "minecraft").Cell(1, 0),
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.MultiplayerPackLoader, Loader, "loader").Visible(Loader.Length > 0).Cell(0, 1),
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.MultiplayerPackSize, ArchiveSize, "size").Visible(ArchiveSize.Length > 0).Cell(1, 1),
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.MultiplayerPackFiles, Files, "files").Visible(Files.Length > 0).Cell(0, 2),
            new MultiplayerPackFact(LauncherAppearance.Capture(),Resources.MultiplayerPackAuthor, Author, "author").Visible(Author.Length > 0).Cell(1, 2),
        ]).ColumnSpacing(20f).RowSpacing(12f).Visible(Info is not null),
    ]).Spacing(16f)).Padding(16f).Background(LauncherTheme.ControlBackground).BorderBrush(LauncherTheme.Edge).BorderThickness(1f);

    protected override void OnDispose() { _controller.Changed -= OnChanged; base.OnDispose(); }
}

/// <summary>Displays a metadata label and its declared value in the pack summary grid.</summary>
/// <param name="Label">The localized metadata role.</param>
/// <param name="Value">The declared or observed value, without a guessed fallback.</param>
/// <param name="Id">The stable metadata role used by native automation.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerPackFact(LauncherAppearance Theme, string Label, string Value, string Id)
{
    /// <summary>Separates the muted label from the value while preserving wrapping and native accessibility.</summary>
    public Ui Body => Column([
        Text(Label).FontSize(11f).Foreground(Theme.Muted),
        Text(Value).FontSize(13f).Foreground(Theme.Ink).Wrap()
            .Automation(new() { Name = Value, AutomationId = "multiplayer.pack." + Id }),
    ]).Spacing(4f);
}