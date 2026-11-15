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

namespace Rodel.MinecraftLauncher.App.Controls.Reports;

internal enum LauncherNoticeKind
{
    Information,
    Success,
    Error
}

/// <summary>Uses the launch-error dialog's selectable, icon-led notice geometry for localized task results.</summary>
/// <param name="Title">Localized status or result heading.</param>
/// <param name="Detail">Selectable localized explanation, including operation-specific limitations.</param>
/// <param name="Kind">Chooses the shared information, success or error appearance.</param>
/// <param name="AutomationId">Stable automation identity for this notice and its child controls.</param>
/// <param name="ActionLabel">Localized label for the optional contextual action.</param>
/// <param name="OnAction">Invokes the optional contextual action without replacing the notice.</param>
/// <param name="ActionEnabled">Controls whether the contextual action is currently available.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct LauncherNotice(
    LauncherAppearance Theme,
    string Title,
    string Detail,
    LauncherNoticeKind Kind,
    string AutomationId,
    string ActionLabel = "",
    Action? OnAction = null,
    bool ActionEnabled = true)
{
    private static void IgnoreAction() { }
    internal Action InvokeAction => OnAction ?? IgnoreAction;
    internal Color Accent => Kind switch
    {
        LauncherNoticeKind.Error => Theme.Error,
        LauncherNoticeKind.Success => Theme.SuccessForeground,
        _ => Theme.Muted,
    };
    internal string Icon => Kind switch
    {
        LauncherNoticeKind.Error => "error",
        LauncherNoticeKind.Success => "success",
        _ => "info"
    };

    /// <summary>Renders selectable status text in the current interface direction using the shared notice geometry.</summary>
    public Ui Body => Border(Grid(
            [GridLength.Pixels(24f), GridLength.Star()],
            [GridLength.Auto],
            [
                LauncherIcons.Line(Icon, 24f, Accent)
                    .Cell(0, 0),
                Column([
                    new SelectableText(Text(Title)
                            .FontSize(14f).FontWeight(600).Foreground(Theme.Ink)
                            .Wrap()).HAlign(HorizontalAlignment.Stretch)
                        .Automation(new()
                        {
                            Name = Title,
                            AutomationId = AutomationId,
                            LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                        }),
                    new SelectableText(Text(Detail)
                            .FontSize(13f).Foreground(Theme.Ink)
                            .Wrap()).HAlign(HorizontalAlignment.Stretch)
                        .Automation(new()
                        {
                            Name = Detail,
                            AutomationId = AutomationId + ".detail"
                        })
                        .Visible(Detail.Length > 0),
                    Button(ActionLabel, InvokeAction).Style(Theme.Buttons.DialogSecondary).Enabled(ActionEnabled)
                        .HAlign(HorizontalAlignment.Right).Visible(OnAction is not null && ActionLabel.Length > 0)
                        .Automation(new() { Name = ActionLabel, AutomationId = AutomationId + ".action" }),
                ])
                    .Spacing(8f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f))
        .Padding(16f)
        .Background(Theme.HighContrast || Kind == LauncherNoticeKind.Information ? Theme.ControlBackground : Accent.MultiplyAlpha(.12f))
        .BorderBrush(Kind == LauncherNoticeKind.Information ? Theme.Edge : Accent.MultiplyAlpha(.4f))
        .BorderThickness(1f);
}