// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Accounts;

internal sealed partial class ExternalAccountDevice(ExternalAccountPopup owner) : Control
{
    private ExternalCodeGlyph[] Characters => [.. (owner.Attempt?.UserCode ?? string.Empty).Select((character, index) => new ExternalCodeGlyph(index, character.ToString(), owner.DeviceEnded))];

    private Ui Body => Column([
        Text(Resources.ExternalDeviceHint)
            .FontSize(14f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Visible(owner.ErrorTitle.Length == 0),
        LauncherControls.OutlineCommand(
            Row([
                Flow(
                    FlowDirection.LeftToRight,
                    Row([
                        Each(Characters, character => character.Index, character => new ExternalCodeLetter(LauncherAppearance.Capture(),character.Text, character.Ended))
                    ]))
                    .Weight(1f),
                LauncherIcons.Line(owner.Copied && !owner.DeviceEnded ? "check" : "copy", 20f)
                    .VAlign(VerticalAlignment.Center),
            ])
                .Spacing(20f),
            () => owner.CopyCode())
            .Style(LauncherButtons.OutlineButton.Padding(16f))
            .Enabled(!owner.DeviceEnded)
            .Automation(new()
            {
                Name = Resources.ExternalCopyCode,
                HelpText = owner.Attempt?.UserCode,
                AutomationId = "accounts.external.code"
            }),
        Row([
            Text(owner.Attempt?.VerificationUri ?? string.Empty)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Weight(1f),
            Text(owner.Copied
                    ? Resources.LaunchErrorCopied
                    : owner.CopyFailed ? Resources.ExternalCopyFailed : string.Empty)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Visible(!owner.DeviceEnded)
                .Automation(new()
                {
                    AutomationId = "accounts.external.code.feedback",
                    LiveSetting = Sprout.Semantics.AutomationLiveSetting.Polite
                }),
        ])
            .Spacing(12f),
        Row([
            Rectangle(LauncherTheme.ProgressAccent)
                .Width(6f).Height(6f)
                .VAlign(VerticalAlignment.Center),
            Text(Resources.ExternalDeviceWaiting)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap(),
        ])
            .Spacing(8f)
            .Visible(!owner.DeviceEnded),
    ])
        .Spacing(16f);
}

/// <summary>One non-interactive character in the device-code row.</summary>
/// <param name = "Index">Stable ordinal within the current code.</param>
/// <param name = "Text">The unmodified character to render.</param>
/// <param name = "Ended">Whether the request ended and the code is no longer usable.</param>
internal sealed record ExternalCodeGlyph(int Index, string Text, bool Ended);

/// <summary>A character occupies one equal flex slot; copying belongs to the encompassing button.</summary>
/// <param name = "Character">The original code character.</param>
/// <param name = "Ended">Whether the request has ended.</param>
/// <param name="Theme">Immutable appearance captured by the owning reactive control or item template.</param>
[View]
internal readonly partial record struct ExternalCodeLetter(LauncherAppearance Theme, string Character, bool Ended)
{
    /// <summary>Renders one character with an equal share of the available code width.</summary>
    public Ui Body => Text(Character)
        .FontFamily("Consolas").FontSize(28f).FontWeight(600).Foreground(Ended ? Theme.Muted : Theme.Ink)
        .HAlign(HorizontalAlignment.Center)
        .Weight(1f);
}