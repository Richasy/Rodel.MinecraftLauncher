// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Input;
using Sprout.Layout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Confirms a one-layer limb replacement without persisting an opt-out when the player cancels.</summary>
internal sealed partial class SkinPartCopyDialog(Action<bool> decide) : Control
{
    internal partial bool SkipWarning { get; private set; }
    internal InteractionNode? CancelFocus => Interaction is { } root ? FocusTraversal.Next(root, current: null, forward: false) : null;

    private Ui Body => Column([
        Column([
            Text(Resources.SkinCopyOpposite).FontSize(20f).FontWeight(600).Foreground(LauncherTheme.Ink).Wrap(),
            Text(Resources.SkinCopyOppositeWarning).FontSize(13f).Foreground(LauncherTheme.Muted).Wrap(),
            new LauncherCheckBox(
                LauncherAppearance.Capture(),
                Resources.SkinCopyOppositeSkipWarning,
                SkipWarning,
                () => SkipWarning = !SkipWarning,
                true,
                "skin.copy-opposite.skip-warning",
                FontSize: 13f,
                BoxSize: 18f),
        ]).Spacing(12f).Padding(24f).MaxWidth(460f),
        Rectangle(LauncherTheme.Edge).Height(1f),
        Grid([GridLength.Star(), GridLength.Star()], [GridLength.Auto], [
            LauncherControls.OutlineCommand(Resources.SkinCopyOpposite, () => decide(true))
                .Style(LauncherButtons.DialogSecondary)
                .Automation(new() { Name = Resources.SkinCopyOpposite, AutomationId = "skin.copy-opposite.confirm" })
                .Cell(0, 0),
            LauncherControls.Command(Resources.DialogCancel, () => decide(false))
                .Style(LauncherButtons.DialogPrimary).AsPrimary()
                .Automation(new() { Name = Resources.DialogCancel, AutomationId = "skin.copy-opposite.cancel" })
                .Cell(1, 0),
        ]).ColumnSpacing(8f).Padding(24f, 18f, 24f, 24f),
    ]);
}