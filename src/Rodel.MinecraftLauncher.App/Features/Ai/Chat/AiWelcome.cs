// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Animation;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using System.Globalization;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiWelcome(AiPage owner) : Control
{
    private Action Connect => owner.OpenSettings;
    private bool Short => owner.Viewport.Height < 700f;

    private Ui Body => Border(Column([
            LauncherIcons.Line("spark", 28f, LauncherTheme.Muted)
                .Visible(!Short),
            Text(Resources.AiEyebrow)
                .FontSize(10f).Foreground(LauncherTheme.Quiet)
                .Visible(!Short),
            Text(owner.Ready ? Resources.AiWelcomeReady : Resources.AiWelcomeEmpty)
                .FontSize(Short ? 20f : 23f).FontWeight(500).Foreground(LauncherTheme.Ink)
                .Wrap()
                .Automation(new() { AutomationId = "ai.welcome.title" }),
            Text(owner.Ready ? Resources.AiWelcomeReadyBody : Resources.AiWelcomeEmptyBody)
                .FontSize(13f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(!Short),
            LauncherControls.OutlineCommand(
                Row([
                    Text(Resources.AiConnect)
                        .FontSize(13f).Foreground(LauncherTheme.Muted)
                ]),
                Connect)
                .Style(LauncherButtons.AiTextAction)
                .Visible(!owner.Ready)
                .Automation(new()
                {
                    Name = Resources.AiConnect,
                    AutomationId = "ai.connect"
                }),
            Column([
                new AiSuggestion(
                    Resources.AiSuggestionConfig,
                    Resources.AiSuggestionConfigNote,
                    () => owner.Suggest(Resources.AiSuggestionConfig),
                    Short,
                    LauncherTheme.Ink,
                    LauncherTheme.Muted,
                    LauncherTheme.Edge,
                    LauncherButtons.AiTextAction,
                    "ai.suggestion.config"),
                new AiSuggestion(
                    Resources.AiSuggestionMods,
                    Resources.AiSuggestionModsNote,
                    () => owner.Suggest(Resources.AiSuggestionMods),
                    Short,
                    LauncherTheme.Ink,
                    LauncherTheme.Muted,
                    LauncherTheme.Edge,
                    LauncherButtons.AiTextAction,
                    "ai.suggestion.mods"),
                new AiSuggestion(
                    Resources.AiSuggestionLogs,
                    Resources.AiSuggestionLogsNote,
                    () => owner.Suggest(Resources.AiSuggestionLogs),
                    Short,
                    LauncherTheme.Ink,
                    LauncherTheme.Muted,
                    LauncherTheme.Edge,
                    LauncherButtons.AiTextAction,
                    "ai.suggestion.logs"),
            ])
                .Spacing(0f)
                .Visible(owner.Ready),
        ])
            .Spacing(Short ? 12f : 18f)
            .MaxWidth(580f)
            .HAlign(HorizontalAlignment.Center))
        .Padding(32f, Short ? 16f : 28f, 32f, 24f);
}