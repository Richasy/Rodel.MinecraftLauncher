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

/// <summary>Shows packaged template previews independently of the scrollable recent-project pane.</summary>
internal sealed partial class SkinStartTemplates(SkinEditorController controller) : Control
{
    private readonly ImageSource[] _previews = [.. SkinEditorController.Templates.Select(id => ImageSource.FromFile(System.IO.Path.Combine(AppContext.BaseDirectory, "Assets", "SkinTemplates", id + ".png")))];

    private Ui Body => LauncherControls.KeyboardGroup(Grid(
        [GridLength.Star(), GridLength.Star(), GridLength.Star()],
        [GridLength.Auto, GridLength.Auto],
        [
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[0], Resources.SkinTemplateClassic, 0).Cell(0, 0),
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[1], Resources.SkinTemplateAlex, 1).Cell(1, 0),
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[2], Resources.SkinTemplateCasual, 2).Cell(2, 0),
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[3], Resources.SkinTemplateFigure, 3).Cell(0, 1),
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[4], Resources.SkinTemplateBlank, 4).Cell(1, 1),
            new SkinStartTemplate(LauncherAppearance.Capture(),controller, _previews[5], Resources.SkinTemplateCape, 5).Cell(2, 1),
        ])
        .ColumnSpacing(12f).RowSpacing(12f));
}

[View]
internal readonly partial record struct SkinStartTemplate(
    LauncherAppearance Theme,
    SkinEditorController Controller,
    ImageSource Preview,
    string Label,
    int Index)
{
    internal Action Choose
    {
        get
        {
            var controller = Controller;
            var index = Index;
            return () => controller.ShowNew(index);
        }
    }

    /// <summary>Shows the actual suggested-model template and opens its preselected creation form.</summary>
    public Ui Body => LauncherControls.Command(
Theme, Column([
            Image(Preview)
                .Stretch(ImageStretch.Uniform)
                .Height(164f)
                .Interpolation(ImageInterpolationMode.HighQualityCubic),
            Text(Label)
                .FontSize(12f)
                .Wrap()
                .HAlign(HorizontalAlignment.Center),
        ])
            .Spacing(12f),
        Choose)
        .Style(Theme.Buttons.Button.Padding(12f))
        .MinHeight(220f)
        .Automation(new()
        {
            Name = Label,
            AutomationId = "skin.start.template." + Index
        });
}

[View]
internal readonly partial record struct SkinStartSectionTitle(LauncherAppearance Theme, string Icon, string Title, string AutomationId)
{
    /// <summary>Aligns section icons and titles with the corresponding recent-project row columns.</summary>
    public Ui Body => Row([
        LauncherIcons.Line(Icon, 18f, Theme.Muted)
            .VAlign(VerticalAlignment.Center),
        Text(Title)
            .FontSize(14f)
            .VAlign(VerticalAlignment.Center)
            .Automation(new()
            {
                Name = Title,
                AutomationId = AutomationId
            }),
    ])
        .Spacing(8f)
        .Padding(12f, 0f);
}