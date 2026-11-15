// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Import;

[View]
internal readonly partial record struct ImportPathInput(
    LauncherAppearance Theme,
    string Label,
    string Value,
    bool IsEnabled,
    Action<string> Change,
    Action Browse,
    string Id,
    string BrowseId,
    string BrowseName,
    TextBoxStyle InputStyle,
    ButtonStyle BrowseStyle,
    bool IsFile = false,
    FileDropHandler? DropHandler = null,
    bool Dragging = false)
{
    /// <summary>Keeps the actual path input and a centered folder picker under their original callbacks and automation identities.</summary>
    public Ui Body => DropTarget(Column([Grid(
        [GridLength.Star(), GridLength.Auto],
        [GridLength.Auto],
        [
            TextBox()
                .Text(Value)
                .ReadOnly()
                .Placeholder(IsFile ? Resources.PathFilePlaceholder : Resources.PathFolderPlaceholder)
                .Style(InputStyle)
                .Enabled(IsEnabled)
                .MinHeight(40f)
                .Automation(new()
                {
                    Name = Label,
                    AutomationId = Id
                })
                .Cell(0, 0),
            LauncherControls.OutlineCommand(
Theme,                Row([
                    LauncherIcons.Line("folder", 18f)
                ]),
                Browse)
                .Style(BrowseStyle)
                .Width(40f).Height(40f)
                .Enabled(IsEnabled)
                .ToolTip(BrowseName)
                .Automation(new()
                {
                    Name = BrowseName,
                    AutomationId = BrowseId
                })
                .Cell(1, 0),
        ])
        .ColumnSpacing(8f), new FileDropHint(Theme,Dragging).Visible(Dragging && DropHandler is not null)]).Spacing(6f), DropHandler ?? FileDropHandler.None);
}