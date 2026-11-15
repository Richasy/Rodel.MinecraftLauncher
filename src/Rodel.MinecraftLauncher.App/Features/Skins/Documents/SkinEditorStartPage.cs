// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Offers real templates and file commands before mounting the editing workspace.</summary>
internal sealed partial class SkinEditorStartPage(SkinEditorState editor) : Control
{
    private readonly SkinStartContent _content = new(editor);

    private Ui Body => Border(_content)
        .Background(LauncherTheme.Background)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 1f, 0f, 0f);
}

internal sealed partial class SkinStartContent(SkinEditorState editor) : Control
{
    private readonly ScrollView _templates = new(new SkinStartTemplates(editor.Controller));
    private readonly SkinRecentProjects _recent = new(editor.Controller);

    private Ui Body => Grid(
        [GridLength.Star()],
        [GridLength.Auto, GridLength.Star()],
        [
            Text(Resources.SkinStartTemplates)
                .FontSize(14f).Foreground(LauncherTheme.Muted)
                .Automation(new()
                {
                    Name = Resources.SkinStartTemplates,
                    AutomationId = "skin.start.templates.title"
                })
                .Cell(0, 0),
            Grid(
                [GridLength.Star(2f), GridLength.Star()],
                [GridLength.Star()],
                [
                    _templates.Automation(new() { AutomationId = "skin.start.templates.scroll" })
                        .Cell(0, 0),
                    Grid(
                        [GridLength.Star()],
                        [GridLength.Auto, GridLength.Star()],
                        [
                            Border(Column([
                                    new SkinStartSectionTitle(LauncherAppearance.Capture(),"folder", Resources.SkinProjectMenu, "skin.start.commands.title"),
                                    Column([
                                        LauncherControls.Command(Resources.SkinNew, () => editor.Controller.ShowNew())
                                            .Style(LauncherButtons.DialogPrimary)
                                            .MinHeight(40f)
                                            .Automation(new() { AutomationId = "skin.start.new" }),
                                        LauncherControls.OutlineCommand(Resources.SkinOpen, editor.Controller.ShowProjects)
                                            .Style(LauncherButtons.DialogSecondary)
                                            .MinHeight(40f)
                                            .Automation(new() { AutomationId = "skin.start.open" }),
                                        LauncherControls.OutlineCommand(Resources.SkinImport, editor.Controller.Files.ShowImport)
                                            .Style(LauncherButtons.DialogSecondary)
                                            .MinHeight(40f)
                                            .Automation(new() { AutomationId = "skin.start.import" }),
                                        LauncherControls.OutlineCommand(Resources.SkinImportCape, editor.Controller.Files.ShowImportCape)
                                            .Style(LauncherButtons.DialogSecondary)
                                            .MinHeight(40f)
                                            .Automation(new() { AutomationId = "skin.start.import-cape" }),
                                    ])
                                        .Spacing(12f),
                                    Text(editor.Status)
                                        .FontSize(11f).Foreground(LauncherTheme.Error)
                                        .Wrap()
                                        .Visible(editor.Status.Length > 0)
                                        .Automation(new()
                                        {
                                            Name = editor.Status,
                                            AutomationId = "skin.start.status"
                                        }),
                                ])
                                    .Spacing(16f))
                                .Padding(16f)
                                .Background(LauncherTheme.ControlBackground)
                                .BorderBrush(LauncherTheme.Edge)
                                .BorderThickness(1f)
                                .Cell(0, 0),
                            _recent.Cell(0, 1),
                        ])
                        .RowSpacing(20f)
                        .Cell(1, 0),
                ])
                .ColumnSpacing(32f)
                .Cell(0, 1),
        ])
        .RowSpacing(16f)
        .MaxWidth(1080f)
        .Margin(new Thickness(36f))
        .HAlign(HorizontalAlignment.Center);
}