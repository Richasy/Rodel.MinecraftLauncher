// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Skins;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

[View]
internal readonly partial record struct SkinRecentProject(LauncherAppearance Theme, SkinEditorController Controller, SkinProjectInfo? Project, DateTimeOffset Now)
{
    internal Action Open
    {
        get
        {
            var controller = Controller;
            var project = Project;
            return () =>
            {
                if (project is not null)
                    _ = controller.OpenAsync(project.DocumentId, project.Name);
            };
        }
    }

    /// <summary>Opens a saved project with an inset icon, aligned metadata and its real modification time.</summary>
    public Ui Body => LauncherControls.Command(
Theme, Grid(
            [GridLength.Pixels(18f), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherIcons.Line("folder", 18f, Theme.Muted)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Column([
                    Text(Project?.Name ?? string.Empty)
                        .FontSize(12f)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed(),
                    Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Project is null ? string.Empty : string.Format(Resources.SkinWorkDetails, Project.Model == Rodel.Minecraft.Skins.SkinModel.Cape
                                        ? Resources.SkinCape
                                        : Project.Model == Rodel.Minecraft.Skins.SkinModel.Slim ? Resources.SkinSlimModel : Resources.SkinClassicModel, Project.Size, Rodel.Minecraft.Skins.SkinLayout.Height(Project.Model, Project.Size)))
                                .FontSize(10f).Foreground(Theme.Muted)
                                .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                                .Cell(0, 0),
                            Text(Project is null ? string.Empty : SkinRecentTime.Format(Project.ModifiedAt, Now))
                                .FontSize(10f).Foreground(Theme.Muted)
                                .Automation(new()
                                {
                                    Name = Project is null ? string.Empty : SkinRecentTime.Accessible(Project.ModifiedAt),
                                    AutomationId = "skin.recent.modified." + Project?.DocumentId
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(8f),
                ])
                    .Spacing(4f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(8f),
        Open)
        .Style(SkinEditorChrome.For(Theme).QuietCommand.Padding(12f, 8f)
            .HorizontalContentAlignment(HorizontalAlignment.Left))
        .MinHeight(60f)
        .Visible(Project is not null)
        .Automation(new()
        {
            Name = Project?.Name ?? string.Empty,
            AutomationId = "skin.recent." + Project?.DocumentId
        });
}