// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Reconcile;
using Sprout.Semantics;
using Sprout.Styling;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Sprout's native button semantics and input, with the launcher's shared bevel skin.</summary>
[ContentHostControl(SemanticsRole.Button, nameof(Label), nameof(OnClick), nameof(LabelContent), IsEnabledField = nameof(IsEnabled))]
public readonly partial record struct LauncherButtonView(
    int StableId,
    ButtonView Inner,
    bool Primary = false,
    bool Selected = false) : IView
{
    public string Label => Inner.Label;
    public Action OnClick => Inner.OnClick;
    public bool IsEnabled => Inner.IsEnabled;
    public TextView LabelContent => new(
        default,
        Label,
        Inner.ResolvedLabelStyle,
        Inner.Resting.Foreground,
        inheritsForeground: true);

    public BevelWidget BuildChrome(Widget content) => new(content, Inner, Primary, Selected);

    /// <summary>Uses native button input to update material colors and the internal pressed face geometry.</summary>
    public InvokableInteraction BuildInteractionNode(ElementId id, Rect bounds, Widget? render)
    {
        var face = (BevelWidget)render!;
        var palette = Inner.Palette;
        return new ButtonInteraction(
            id,
            bounds,
            palette.Normal,
            palette.PointerOver,
            palette.Pressed,
            palette.Disabled,
            IsEnabled,
            face.SetBackground,
            face.SetForeground,
            face.SetBorder,
            setIconState: face.SetInteractionState,
            focusable: Inner.Focusable)
        {
            Invoked = OnClick,
            FocusVisualCornerRadius = CornerRadius.Zero,
            FocusVisualStyle = Inner.FocusVisual,
            GotFocus = Inner.GotFocus,
            LostFocus = Inner.LostFocus,
        };
    }

    public void SyncInteraction(InvokableInteraction node) => Inner.SyncInteraction(node);

    public WidgetPatch DiffChrome(LauncherButtonView previous) => previous.Inner.Appearance == Inner.Appearance && previous.IsEnabled == IsEnabled && previous.Primary == Primary && previous.Selected == Selected ? WidgetPatch.Empty : new([new SetBevelConfiguration(Inner, Primary, Selected)]);
}

internal static class LauncherButtonModifiers
{
    public static LauncherButtonView OnGotFocus(this LauncherButtonView view, Action<FocusState> action) => view with
    {
        Inner = view.Inner.OnGotFocus(action)
    };

    public static LauncherButtonView OnLostFocus(this LauncherButtonView view, Action action) => view with
    {
        Inner = view.Inner.OnLostFocus(action)
    };

    public static LauncherButtonView Style(this LauncherButtonView view, ButtonStyle style) => view with
    {
        Inner = view.Inner.Style(style),
    };

    /// <summary>Chooses accent or neutral bevel colors without changing the button's layout.</summary>
    public static LauncherButtonView AsPrimary(this LauncherButtonView view, bool primary = true) => view with
    {
        Primary = primary,
    };

    public static LauncherButtonView AsSelected(this LauncherButtonView view, bool selected) => view with
    {
        Selected = selected,
    };

    public static LauncherButtonView Enabled(this LauncherButtonView view, bool enabled) => view with
    {
        Inner = view.Inner.Enabled(enabled),
    };
}