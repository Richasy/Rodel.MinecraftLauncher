// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Styling;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiServiceFields(AiServiceDialog owner) : Control
{
    private readonly AiServiceCatalog _catalog = new(owner);

    private Ui Body => Column([
        InfoBar(message: owner.Error)
            .Severity(InfoBarSeverity.Error)
            .Closable(false)
            .Style(default(InfoBarStyle).CornerRadius(0f).MessageFontSize(12f))
            .Open(owner.Error.Length > 0)
            .Automation(new()
            {
                Name = owner.Error,
                AutomationId = "ai.service.error"
            }),
        Column([
            Text(Resources.AiDisplayName)
                .Foreground(LauncherTheme.Ink).FontSize(13f),
            TextBox(owner.Name, value => owner.Name = value)
                .Style(LauncherStyles.Input)
                .Automation(new()
                {
                    Name = Resources.AiDisplayName,
                    AutomationId = "ai.service.name"
                }),
            new LauncherCheckBox(
LauncherAppearance.Capture(),                Resources.AiMcpEnable,
                owner.Enabled,
                () => owner.Enabled = !owner.Enabled,
                true,
                "ai.service.enabled").Visible(owner.Kind != 3),
            Text(Resources.AiServiceKind)
                .Foreground(LauncherTheme.Ink).FontSize(13f),
            LauncherControls.Choice(AiProtocols.IsLocalInferenceAvailable
                    ? [Resources.AiOpenAiKind, Resources.AiAnthropicKind, Resources.AiGeminiKind, Resources.AiLocalKind]
                    : [Resources.AiOpenAiKind, Resources.AiAnthropicKind, Resources.AiGeminiKind]).MinHeight(40f)
                .SelectedIndex(owner.Kind)
                .OnSelectionChanged(owner.ChangeKind)
                .Automation(new()
                {
                    Name = Resources.AiServiceKind,
                    AutomationId = "ai.service.kind"
                }),
            Text(owner.KindHelp)
                .FontSize(11f).Foreground(LauncherTheme.Muted)
                .Wrap(),
            Text(Resources.AiProtocol)
                .FontSize(13f).Foreground(LauncherTheme.Ink)
                .Visible(owner.Kind == 0),
            LauncherControls.Choice(["Responses API", "Chat Completions API"]).MinHeight(40f)
                .SelectedIndex(owner.Protocol)
                .OnSelectionChanged(value => owner.Protocol = value)
                .Visible(owner.Kind == 0)
                .Automation(new()
                {
                    Name = Resources.AiProtocol,
                    AutomationId = "ai.service.protocol"
                }),
            Text(Resources.AiApiKey)
                .FontSize(13f).Foreground(LauncherTheme.Ink)
                .Visible(owner.Kind != 3),
            PasswordBox(owner.Secret, value => owner.Secret = value)
                .Palette(LauncherStyles.InputPalette)
                .Padding(LauncherStyles.InputPadding)
                .FontSize(14f)
                .CornerRadius(0f)
                .Visible(owner.Kind != 3)
                .Automation(new()
                {
                    Name = Resources.AiApiKey,
                    AutomationId = "ai.service.key"
                }),
            Text(owner.Kind == 3 ? Resources.AiLocalFile : Resources.AiEndpoint)
                .FontSize(13f).Foreground(LauncherTheme.Ink)
                .Visible(owner.NeedsEndpoint),
            new FileSystemPathInput(
                owner.Window,
                owner.Endpoint,
                value => owner.ChooseLocalFile(value),
                Resources.AiLocalFile,
                "ai.service.path",
                Enabled: !owner.Working,
                Extensions: [".gguf"],
                Placeholder: Resources.AiLocalFilePlaceholder).Visible(owner.Kind == 3 && owner.NeedsEndpoint),
            TextBox(owner.Endpoint, value => owner.Endpoint = value)
                .Style(LauncherStyles.Input)
                .Visible(owner.Kind != 3)
                .Automation(new()
                {
                    Name = Resources.AiEndpoint,
                    AutomationId = "ai.service.endpoint"
                }),
            new LauncherCheckBox(
LauncherAppearance.Capture(),                Resources.AiAllowHttp,
                owner.AllowHttp,
                () => owner.AllowHttp = !owner.AllowHttp,
                true,
                "ai.service.http").Visible(owner.Kind != 3),
        ])
            .Spacing(10f)
            .Visible(owner.Step == 0),
        Column([
            _catalog,
            Text(Resources.AiModelsNotLoaded)
                .FontSize(12f).Foreground(LauncherTheme.Muted)
                .Wrap()
                .Visible(owner.Discovered.Length == 0 && !owner.Working && !owner.Manual && owner.Code.Length == 0 && owner.Error.Length == 0),
            LauncherControls.OutlineCommand(
                Row([
                    LauncherIcons.Line("plus", 16f),
                    Text(Resources.AiManualModel)
                        .FontSize(12f)
                ])
                    .Spacing(8f),
                owner.EnterModelManually)
                .Style(LauncherButtons.DialogSecondary.Padding(12f, 8f))
                .HAlign(HorizontalAlignment.Left)
                .Visible(!owner.Manual && owner.Code.Length == 0)
                .Automation(new()
                {
                    Name = Resources.AiManualModel,
                    AutomationId = "ai.service.manual"
                }),
            Border(Grid(
                    [GridLength.Star(), GridLength.Auto],
                    [GridLength.Auto],
                    [
                        Column([
                            Text(Resources.AiDisplayName)
                                .FontSize(13f).Foreground(LauncherTheme.Ink),
                            TextBox(owner.ModelName, value => owner.ModelName = value)
                                .Style(LauncherStyles.Input)
                                .Automation(new()
                                {
                                    Name = Resources.AiDisplayName,
                                    AutomationId = "ai.model.name"
                                }),
                            Text(Resources.AiModelNameHelp)
                                .FontSize(11f).Foreground(LauncherTheme.Muted)
                                .Wrap(),
                            Text(owner.Kind == 3 ? Resources.AiLocalFile : Resources.AiModelIdentifier)
                                .FontSize(13f).Foreground(LauncherTheme.Ink),
                            new FileSystemPathInput(
                                owner.Window,
                                owner.Code,
                                value => owner.Code = value,
                                Resources.AiLocalFile,
                                "ai.model.path",
                                Enabled: !owner.Working,
                                Extensions: [".gguf"],
                                Placeholder: Resources.AiLocalFilePlaceholder).Visible(owner.Kind == 3),
                            TextBox(owner.Code, value => owner.Code = value)
                                .Style(LauncherStyles.Input)
                                .Visible(owner.Kind != 3)
                                .Automation(new()
                                {
                                    Name = Resources.AiModelIdentifier,
                                    AutomationId = "ai.model.code"
                                }),
                        ])
                            .Spacing(8f)
                            .Cell(0, 0),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("delete", 18f, LauncherTheme.Error)
                            ]),
                            () =>
                            {
                                owner.Code = string.Empty;
                                owner.ModelName = string.Empty;
                                owner.Manual = false;
                            })
                            .Style(LauncherButtons.PopupCloseButton)
                            .Width(32f).Height(32f)
                            .ToolTip(Resources.AiRemoveModelInput)
                            .Automation(new()
                            {
                                Name = Resources.AiRemoveModelInput,
                                AutomationId = "ai.model.clear"
                            })
                            .Cell(1, 0),
                    ])
                    .ColumnSpacing(12f))
                .Padding(16f)
                .Background(LauncherTheme.ControlBackground)
                .BorderBrush(LauncherTheme.Edge)
                .BorderThickness(1f)
                .Visible(owner.Manual || owner.Code.Length > 0),
        ])
            .Spacing(14f)
            .Visible(owner.Step == 1),
    ])
        .Spacing(16f);
}

internal sealed partial class AiServiceCatalog(AiServiceDialog owner) : Control
{
    private Ui Body => Column([
        ItemsRepeater(
            owner.Discovered,
            static model => model.Code,
            model => new AiServiceModelChoice(owner, model),
            static (row, model) => row.Update(model),
            new Sprout.Widgets.StackLayout { EstimatedItemExtent = 72f }).KeyboardNavigation(),
    ])
        .Spacing(8f);
}