// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Skins;

/// <summary>Keeps the editable input in its own retained subtree while streaming content and composer actions change.</summary>
internal sealed partial class SkinAiPrompt(SkinEditorAiPanel owner) : Control
{
    private string Draft
    {
        get
        {
            _ = owner.Revision;
            return owner.Conversation?.Draft ?? string.Empty;
        }
    }

    private Ui Body => Column([
        TextBox(Draft, value => owner.ChangeDraft(value))
            .AcceptsReturn()
            .SubmitOnEnter()
            .OnSubmitted(_ => owner.SubmitDraft())
            .Wrap()
            .ClearButton(false)
            .Placeholder(Resources.SkinAiPrompt)
            .Style(LauncherStyles.Input)
            .Palette(LauncherStyles.InputPalette with { FocusedElevation = ElevationProfile.TextField(LauncherTheme.Focus, LauncherTheme.Focus) })
            .Height(80f)
            .Enabled(owner.PromptEnabled)
            .Automation(new()
            {
                Name = Resources.SkinAiPrompt,
                AutomationId = "skin.ai.prompt"
            }),
    ]);
}