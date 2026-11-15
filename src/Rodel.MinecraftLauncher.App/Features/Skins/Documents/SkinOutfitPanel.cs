// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Shows the shared character selector and import commands inside the cape inspector.</summary>
internal sealed partial class SkinOutfitPanel(SkinEditorState editor) : Control
{
    private SkinEditorOutfit Outfit => editor.Controller.Outfit;
    private bool HasChoices => Outfit.Choices.Length > 0;
    private bool Available => editor.Controller.Files.Available;
    private string[] Names => [Resources.SkinNoOutfit, .. Outfit.Choices.Select(work => work.Name)];
    private int SelectedIndex => Outfit.Work is { } work
        ? Array.FindIndex(Outfit.Choices, choice => choice.DocumentId == work.DocumentId) + 1
        : -1;

    private void Choose(int index)
    {
        var choices = Outfit.Choices;
        if (index < 0 || index > choices.Length)
            return;
        var work = index == 0 ? null : choices[index - 1];
        DispatcherQueue.GetForCurrentThread().TryEnqueue(() => _ = Outfit.SelectAsync(work));
    }

    private Ui Body => Border(Column([
            LauncherControls.OutlineCommand(Resources.SkinImportCompanion, editor.Controller.Files.ShowImportCompanion)
                .Style(LauncherButtons.DialogSecondary.Padding(8f, 6f)
                    .LabelStyle(LauncherStyles.Text.FontSize(12f)))
                .HAlign(HorizontalAlignment.Stretch)
                .Enabled(Available)
                .Automation(new()
                {
                    Name = Resources.SkinImportCompanion,
                    AutomationId = "skin.outfit.import"
                })
                .Visible(!HasChoices),
            Column([
                Text(Resources.SkinCape)
                    .FontSize(13f).FontWeight(600),
                Text(Resources.SkinOutfitHelp)
                    .FontSize(11f).Foreground(LauncherTheme.Muted)
                    .Wrap(),
                Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        LauncherControls.Choice(Names)
                            .Placeholder(Resources.SkinChooseOutfit)
                            .SelectedIndex(SelectedIndex)
                            .OnSelectionChanged(Choose)
                            .HAlign(HorizontalAlignment.Stretch)
                            .Enabled(Available)
                            .Automation(new()
                            {
                                Name = Resources.SkinChooseOutfit,
                                AutomationId = "skin.outfit.choose"
                            })
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("skin-outfit-add", 18f)
                            ]),
                            editor.Controller.Files.ShowImportCompanion)
                            .Style(LauncherButtons.DialogSecondary.Padding(0f, 0f))
                            .Width(36f)
                            .VAlign(VerticalAlignment.Stretch)
                            .Enabled(Available)
                            .ToolTip(Resources.SkinImportCompanion)
                            .Automation(new()
                            {
                                Name = Resources.SkinImportCompanion,
                                AutomationId = "skin.outfit.import.icon"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(8f),
                Grid(
                    [GridLength.Star(), GridLength.Star()],
                    [GridLength.Auto],
                    [
                        new LauncherCheckBox(
                            LauncherAppearance.Capture(),
                            Resources.SkinSolo,
                            Outfit.Solo,
                            () => Outfit.Solo = !Outfit.Solo,
                            Available,
                            "skin.outfit.solo",
                            FontSize: 11f)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(Resources.SkinSwitchWork, Outfit.OpenWork)
                            .Style(LauncherButtons.DialogSecondary.Padding(8f, 6f)
                                .LabelStyle(LauncherStyles.Text.FontSize(11f)))
                            .Enabled(Available)
                            .Automation(new() { AutomationId = "skin.outfit.switch" })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(8f)
                    .Visible(Outfit.Work is not null),
            ])
                .Spacing(10f)
                .Visible(HasChoices),
        ]))
        .Padding(editor.SectionPadding)
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(0f, 0f, 0f, 1f);
}