// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Svg;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiRegistrationList(AiSettingsSection owner, string kind) : Control
{
    private string SkillsLabel => string.Join(" · ", owner.State.AvailableSkills.Select(skill => skill.Name));
    private AiUiRegistration[] Entries => [..(kind == "folder"
            ? owner.State.Folders
            : kind == "skill" ? owner.State.Skills : owner.State.Extensions)];

    private Ui Body => Column([
        Border(Column([
                Text(Resources.AiFolders)
                    .Align(TextAlignment.Center)
                    .FontSize(14f).FontWeight(600).Foreground(LauncherTheme.Ink),
                Text(Resources.AiBuiltinFoldersDescription)
                    .Align(TextAlignment.Center)
                    .FontSize(12f).Foreground(LauncherTheme.Muted)
                    .Wrap()
            ])
                .Spacing(10f))
            .Padding(16f)
            .Visible(kind == "folder" && Entries.Length == 0),
        Text(Resources.AiNoEntries)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(Entries.Length == 0 && kind is not ("skill" or "folder")),
        Text(SkillsLabel)
            .FontSize(12f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(kind == "skill"),
        ItemsRepeater(
            Entries,
            static item => item.Id,
            item => new AiRegistrationRow(owner, item, kind),
            static (row, item) => row.SetItem(item),
            new StackLayout { EstimatedItemExtent = 70f }).KeyboardNavigation(),
    ]);
}

internal sealed partial class AiRegistrationRow(AiSettingsSection owner, AiUiRegistration initial, string kind) : Control
{
    private partial AiUiRegistration Item { get; set; }

    internal void SetItem(AiUiRegistration value) => Item = value;
    private AiUiRegistration CurrentItem => Item ?? initial;
    private bool Builtin => CurrentItem.Id is "builtin:app" or "builtin:data";
    private string Title => CurrentItem.Id == "builtin:app"
        ? Resources.AiApplicationFolder
        : CurrentItem.Id == "builtin:data" ? Resources.AiDataFolder : CurrentItem.Name;
    private MenuFlyout? _menu;

    private Ui Body => Border(Grid(
            [GridLength.Star(), GridLength.Auto, GridLength.Auto],
            [GridLength.Auto],
            [
                Column([
                    Text(Title)
                        .FontSize(13f).Foreground(LauncherTheme.Ink),
                    Text(CurrentItem.Path)
                        .FontSize(11f).Foreground(LauncherTheme.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                ])
                    .Spacing(5f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Text(CurrentItem.Mode == "write" ? Resources.AiFolderReadWrite : Resources.AiReadOnly)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(kind == "folder")
                    .Cell(1, 0),
                ToggleSwitch(CurrentItem.Enabled, SetEnabled)
                    .NoStatusText()
                    .Palette(LauncherStyles.ContentToggle)
                    .OnSurfacePaint(LauncherToggleSurface.Paint)
                    .Visible(kind != "folder")
                    .Automation(new()
                    {
                        Name = CurrentItem.Name,
                        AutomationId = "ai.entry.toggle." + CurrentItem.Id
                    })
                    .Cell(1, 0),
                LauncherControls.OutlineCommand(
                    Row([
                        LauncherIcons.Line("more", 17f)
                    ]),
                    ShowMenu)
                    .Style(LauncherButtons.PopupCloseButton)
                    .Width(32f).Height(32f)
                    .ToolTip(Resources.AiManageItem)
                    .Automation(new()
                    {
                        Name = Resources.AiManageItem,
                        AutomationId = "ai.entry.more." + CurrentItem.Id
                    })
                    .Visible(!Builtin)
                    .Cell(2, 0),
                Text(Resources.AiBuiltin)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Visible(Builtin)
                    .Cell(2, 0),
            ])
            .ColumnSpacing(12f))
        .Padding(16f, 12f)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, HasFollowingEntry ? 1f : 0f);
    private bool HasFollowingEntry => Entries.Count > 0 && Entries[^1].Id != CurrentItem.Id;
    private IReadOnlyList<AiUiRegistration> Entries => kind == "folder"
        ? owner.State.Folders
        : kind == "skill" ? owner.State.Skills : owner.State.Extensions;

    private void SetEnabled(bool value)
    {
        _ = owner.State.EnableEntryAsync(kind, CurrentItem.Id, value);
    }

    private void ShowMenu()
    {
        _menu = new MenuFlyout(new MenuFlyoutItem(Resources.AiEdit, () => owner.OpenEntry(kind, CurrentItem)), new MenuFlyoutItem(Resources.AiRemove, () => _ = owner.State.RemoveEntryAsync(kind, CurrentItem.Id)) { Icon = LauncherIcons.Element("delete", 17f, LauncherTheme.Error) }).Style(LauncherMenus.CommandMenu);
        _menu.ShowAt(owner.Window);
    }

    protected override void OnDispose()
    {
        _menu?.Hide();
        base.OnDispose();
    }
}