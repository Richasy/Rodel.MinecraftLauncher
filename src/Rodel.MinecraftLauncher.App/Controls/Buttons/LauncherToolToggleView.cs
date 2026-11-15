// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Graphics;
using Sprout.Input;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Semantics;
using Sprout.Widgets;

namespace Rodel.MinecraftLauncher.App.Controls.Buttons;

/// <summary>Combines native toggle semantics with the launcher's ordinary command bevel.</summary>
/// <param name = "Inner">The native controlled toggle and its theme palette.</param>
/// <param name="Chrome">The immutable command appearance captured by the owning reactive view.</param>
[ToggleContentHostControl(SemanticsRole.Button, nameof(Label), nameof(Toggle), nameof(IsChecked), nameof(LabelContent), IsEnabledField = nameof(IsEnabled))]
public readonly partial record struct LauncherToolToggleView(ToggleButtonView Inner, Sprout.Styling.ButtonStyle Chrome) : IView
{
    /// <summary>Gets the accessible label.</summary>
    public string Label => Inner.Label;
    /// <summary>Gets the selected tool state.</summary>
    public bool? IsChecked => Inner.IsChecked;
    /// <summary>Gets whether input is available.</summary>
    public bool IsEnabled => Inner.IsEnabled;
    /// <summary>Gets the native controlled activation callback.</summary>
    public Action Toggle
    {
        get
        {
            var inner = Inner;
            return () => inner.Toggled?.Invoke(CheckBoxToggle.Next(inner.IsChecked, inner.IsThreeState));
        }
    }

    /// <summary>Gets fallback text for a tool without projected content.</summary>
    public TextView LabelContent => new(
        default,
        Label,
        Inner.ResolvedLabelStyle,
        Inner.Resting.Foreground,
        inheritsForeground: true);
    /// <summary>Gets the render-reconciled surface using the shared ordinary-button bevel.</summary>
    public LauncherButtonView BoxView
    {
        get
        {
            var face = Sprout.Markup.Button(string.Empty, Toggle).Enabled(IsEnabled).Style(Chrome);
            return new(0, face, Selected: IsChecked == true);
        }
    }

    /// <summary>Builds the render-reconciled bevel beneath the projected icon.</summary>
    public OverlayPanel BuildChrome(Widget content)
    {
        var overlay = new OverlayPanel
        {
            IsForegroundScopeBoundary = true
        };
        overlay.Add(GeneratedReconciler.Build(BoxView));
        content.SetLayoutProps(content.IntrinsicLayoutProps with
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });
        overlay.Add(content);
        return overlay;
    }

    /// <summary>Preserves native pointer, keyboard and UIA toggle activation.</summary>
    public InvokableInteraction BuildInteractionNode(ElementId id, Rect bounds, Widget? render)
    {
        var overlay = (OverlayPanel)render!;
        var face = (BevelWidget)overlay.Children[0];
        var foreground = new ContentForegroundCascade(overlay.Children[1]);
        return new ToggleButtonInteraction(
            id,
            bounds,
            Inner.Palette.Row(IsChecked),
            IsEnabled,
            face.SetBackground,
            (border, _) => face.SetBorder(border.AsSolid ?? LauncherTheme.Edge, null),
            color =>
            {
                face.SetForeground(color.AsSolid ?? LauncherTheme.Ink);
                foreground.Apply(color);
            })
        {
            Invoked = Toggle,
            FocusVisualCornerRadius = CornerRadius.Zero,
            FocusVisualStyle = Inner.FocusVisual,
        };
    }

    /// <summary>Refreshes native checked-state visuals and callbacks after selection changes.</summary>
    public void SyncInteraction(InvokableInteraction node) => Inner.SyncInteraction(node);
}