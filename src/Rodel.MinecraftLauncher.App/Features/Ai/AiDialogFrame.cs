// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Provides the shared fixed dialog header/footer and an edge-aligned scrolling body.</summary>
[View]
internal readonly partial record struct AiDialogFrame(
    LauncherAppearance Theme,
    AiDialogAppearance Appearance,
    string Title,
    Control Content,
    Action Close,
    string Primary,
    Action Submit,
    bool Enabled,
    float Height,
    string Secondary = "",
    Action? Back = null,
    bool SingleAction = false,
    Action? HeaderAction = null,
    string HeaderLabel = "",
    string HeaderAutomationId = "",
    bool HeaderEnabled = true)
{
    /// <summary>Builds the current localized AI presentation using the launcher control roles.</summary>
    public Ui Body => Border(Grid(
            [GridLength.Star()],
            [GridLength.Auto, GridLength.Auto, GridLength.Auto],
            [
                Border(Grid(
                        [GridLength.Star(), GridLength.Auto],
                        [GridLength.Auto],
                        [
                            Text(Title)
                                .FontSize(18f).FontWeight(700).Foreground(Appearance.Ink)
                                .Wrap()
                                .VAlign(VerticalAlignment.Center)
                                .Automation(new()
                                {
                                    Name = Title,
                                    AutomationId = "ai.dialog.title"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Theme, HeaderLabel, HeaderAction ?? Close)
                                .Style(Appearance.Secondary.Padding(12f, 8f))
                                .VAlign(VerticalAlignment.Center)
                                .Visible(HeaderAction is not null)
                                .Enabled(HeaderEnabled)
                                .Automation(new()
                                {
                                    Name = HeaderLabel,
                                    AutomationId = HeaderAutomationId
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(20f, 16f)
                    .Cell(0, 0),
                ScrollView(Content)
                    .Padding(new Thickness(20f, 0f, 20f, 20f))
                    .MaxHeight(Math.Max(1f, Height - (HeaderAction is null ? 136f : 152f)))
                    .Automation(new()
                    {
                        Name = Title,
                        AutomationId = "ai.dialog.body"
                    })
                    .Cell(0, 1),
                Border(Grid(
                        [GridLength.Star(), GridLength.Star()],
                        [GridLength.Auto],
                        [
                            LauncherControls.Command(Theme,Primary, Submit)
                                .Style(Appearance.Primary)
                                .Enabled(Enabled)
                                .Visible(!SingleAction)
                                .Automation(new()
                                {
                                    Name = Primary,
                                    AutomationId = "ai.dialog.submit"
                                })
                                .Cell(0, 0),
                            LauncherControls.OutlineCommand(Theme,SingleAction
                                    ? Resources.AiClose
                                    : Secondary.Length > 0 ? Secondary : Resources.AiCancel, Back ?? Close)
                                .Style(Appearance.Secondary)
                                .Automation(new()
                                {
                                    Name = SingleAction
                                        ? Resources.AiClose
                                        : Secondary.Length > 0 ? Secondary : Resources.AiCancel,
                                    AutomationId = "ai.dialog.secondary"
                                })
                                .Cell(1, 0),
                        ])
                        .ColumnSpacing(12f))
                    .Padding(20f, 14f, 20f, 18f)
                    .BorderBrush(Appearance.Edge)
                    .BorderThickness(0f, 1f, 0f, 0f)
                    .Cell(0, 2),
            ]))
        .BorderBrush(Appearance.Edge)
        .BorderThickness(1f)
        .MaxHeight(Height);
}

/// <summary>Captures dialog material roles by value so an existing open form can follow a theme change.</summary>
internal readonly record struct AiDialogAppearance(
    Brush Surface,
    Color Ink,
    Color Edge,
    ButtonStyle Primary,
    ButtonStyle Secondary)
{
    internal static AiDialogAppearance Capture() => new(
        LauncherMaterials.Acrylic,
        LauncherTheme.Ink,
        LauncherTheme.Edge,
        LauncherButtons.DialogPrimary,
        LauncherButtons.DialogSecondary);
}