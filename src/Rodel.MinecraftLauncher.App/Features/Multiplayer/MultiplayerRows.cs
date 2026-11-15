// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Multiplayer;

/// <summary>Displays the actual network state independently from game membership.</summary>
/// <param name="Label">Localized state from the Core room snapshot.</param>
/// <param name="Ready">Whether the network connection is currently usable.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerRoomStatus(LauncherAppearance Theme, string Label, bool Ready)
{
    /// <summary>Pairs a small status marker with the readable state in the fixed room header.</summary>
    public Ui Body => Row([
        Rectangle(Ready ? Theme.ProgressAccent : Theme.Muted).Width(6f).Height(6f).VAlign(VerticalAlignment.Center),
        Text(Label).FontSize(12f).Foreground(Ready ? Theme.ProgressAccent : Theme.Muted).Wrap()
            .Automation(new() { Name = Label, AutomationId = "multiplayer.room.state" }),
    ]).Spacing(8f);
}

/// <summary>Displays one compact leading icon and two text roles.</summary>
/// <param name="Icon">The shared launcher icon key.</param>
/// <param name="Title">The primary displayed value.</param>
/// <param name="Detail">The secondary displayed description.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerSummary(LauncherAppearance Theme, string Icon, string Title, string Detail)
{
    /// <summary>Aligns the 40-DIP leading artwork and the text origin across worlds and members.</summary>
    public Ui Body => Grid([GridLength.Pixels(40f), GridLength.Star()], [GridLength.Auto], [
        Border(Overlay([LauncherIcons.Line(Icon, 28f).HAlign(HorizontalAlignment.Center).VAlign(VerticalAlignment.Center)]))
            .Background(Theme.ControlBackground).Width(40f).Height(40f).VAlign(VerticalAlignment.Center).Cell(0, 0),
        Column([
            Text(Title).FontSize(14f).Foreground(Theme.Ink).Wrap().Automation(new() { Name = Title }),
            Text(Detail).FontSize(12f).Foreground(Theme.Muted).Wrap().Automation(new() { Name = Detail }),
        ]).Spacing(4f).VAlign(VerticalAlignment.Center).Cell(1, 0),
    ]).ColumnSpacing(14f);
}

/// <summary>Displays a native network participant without inferring a game skin.</summary>
/// <param name="Member">The current Core participant snapshot.</param>
/// <param name="Divider">Whether another participant precedes this row.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerMemberView(LauncherAppearance Theme, MultiplayerMember Member, bool Divider)
{
    /// <summary>Displays a neutral avatar because native network profiles do not establish a skin identity.</summary>
    public Ui Body => Border(new MultiplayerSummary(Theme, "person", Member.Name, Member.Role == "host" ? Resources.MultiplayerHost : Resources.MultiplayerMember)
        .Margin(new Thickness(24f, 16f, 24f, 16f))).BorderBrush(Theme.Edge).BorderThickness(0f, Divider ? 1f : 0f, 0f, 0f);
}

/// <summary>Creates a named compact icon action for the room workspace.</summary>
/// <param name="Icon">The launcher icon key.</param>
/// <param name="Name">The localized accessible name and purpose tooltip.</param>
/// <param name="Click">The action invoked by the player.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct MultiplayerIcon(LauncherAppearance Theme, string Icon, string Name, Action Click)
{
    /// <summary>Provides a compact icon command with a purpose tooltip and accessible name.</summary>
    public Ui Body => LauncherControls.OutlineCommand(Theme, Row([LauncherIcons.Line(Icon, 18f)]), Click)
        .Style(Theme.Buttons.IconButton).Width(32f).ToolTip(Name)
        .Automation(new() { Name = Name, AutomationId = "multiplayer." + Icon });
}

internal sealed partial class MultiplayerAbout : Control
{
    private static string Attribution => Resources.MultiplayerAboutText.Split('\n', 2)[0].Trim();
    private static string Description => Resources.MultiplayerAboutText.Contains('\n') ? Resources.MultiplayerAboutText.Split('\n', 2)[1].Trim() : string.Empty;
    private Ui Body => Column([
        Text(Attribution).FontSize(12f).Foreground(LauncherTheme.Muted).Wrap().Automation(new() { Name = Attribution, AutomationId = "multiplayer.about.attribution" }),
        Text(Description).FontSize(13f).Foreground(LauncherTheme.Ink).Wrap().Visible(Description.Length > 0)
            .Automation(new() { Name = Description, AutomationId = "multiplayer.about.storage" }),
    ]).Spacing(12f).Width(320f);
}

/// <summary>Retains the explanation button's own placement target instead of relying on another field's focus.</summary>
internal sealed partial class MultiplayerAboutButton(Action open) : Control
{
    private Ui Body => new MultiplayerIcon(LauncherAppearance.Capture(), "info", Resources.MultiplayerAbout, open);
}

internal sealed partial class MultiplayerDifferences : Control
{
    private partial PackReview? Review { get; set; }
    internal MultiplayerDifferences(PackReview? review) => Review = review;
    private Ui Body => ItemsRepeater(Review?.Actions ?? [], static change => change.Path, static change => new MultiplayerChangeRow(LauncherAppearance.Capture(), change), new StackLayout { EstimatedItemExtent = 54f });
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source) => Review = ((MultiplayerDifferences)source).Review;
}

[View]
internal readonly partial record struct MultiplayerChangeRow(LauncherAppearance Theme, PackFileChange Change)
{
    /// <summary>Shows frozen file decisions from the Core pack review.</summary>
    public Ui Body => Column([
        Text(Change.Path).FontSize(13f).Foreground(Theme.Ink).Wrap(),
        Text(Change.Reason).FontSize(12f).Foreground(Theme.Muted).Wrap(),
    ]).Spacing(4f).Margin(new Thickness(0f, 0f, 0f, 12f));
}

internal sealed record MultiplayerMissingEntry(PackMissingFile File, string Pending);

internal sealed partial class MultiplayerMissingFiles : Control
{
    private readonly MultiplayerController _controller;
    private readonly Window _window;
    private readonly Func<MultiplayerMissingEntry, MultiplayerMissingRow> _factory;
    private partial int Revision { get; set; }
    internal MultiplayerMissingFiles(MultiplayerController controller, Window window)
    { _controller = controller; _window = window; _factory = Row; controller.Changed += OnChanged; }
    private void OnChanged() => Revision++;
    private MultiplayerMissingEntry[] Rows
    {
        get
        {
            _ = Revision;
            return [.. _controller.Pack.State.MissingFiles.Select(file => new MultiplayerMissingEntry(file,
                _controller.SelectedFiles.TryGetValue(file.ManualKey ?? file.Path!, out var name) ? string.Format(Resources.MultiplayerPendingFile, name) : string.Empty))];
        }
    }
    private Ui Body => ItemsRepeater(Rows, static entry => entry.File.ManualKey ?? entry.File.Path!, _factory, new StackLayout { EstimatedItemExtent = 98f }).KeyboardNavigation(singleTabStop: false);
    private MultiplayerMissingRow Row(MultiplayerMissingEntry entry) => new(LauncherAppearance.Capture(), entry, () => _ = PickAsync(entry.File), !_controller.Pack.State.Active);

    private async Task PickAsync(PackMissingFile missing)
    {
        try
        {
            var picker = _window.CreateFileOpenPicker(); picker.FileTypeFilter.Add("*");
            if (await picker.PickSingleFileAsync() is { } file) await _controller.StageFileAsync(missing, file.Path);
        }
        catch (Exception error) { _controller.ReportError(error.Message); }
    }
    protected override void OnDispose() { _controller.Changed -= OnChanged; base.OnDispose(); }
}

[View]
internal readonly partial record struct MultiplayerMissingRow(LauncherAppearance Theme, MultiplayerMissingEntry Entry, Action Pick, bool Editable)
{
    /// <summary>Keeps the local-file action inside its interrupted download, with a pending verification label.</summary>
    public Ui Body => Column([
        Text(Entry.File.Path ?? Entry.File.ManualFileName ?? Entry.File.ManualKey ?? string.Empty).FontSize(13f).Foreground(Theme.Ink).Wrap(),
        Text(Entry.Pending).FontSize(12f).Foreground(Theme.Muted).Wrap().Visible(Entry.Pending.Length > 0),
        LauncherControls.OutlineCommand(Theme,Resources.MultiplayerPickFile, Pick).Enabled(Editable),
    ]).Spacing(8f).Margin(new Thickness(0f, 0f, 0f, 12f));
}