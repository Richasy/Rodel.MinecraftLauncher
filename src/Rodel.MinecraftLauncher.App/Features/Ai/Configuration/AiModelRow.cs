// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls;
using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reactive;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Svg;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

internal sealed partial class AiModelRow : Control
{
    private readonly AiSettingsSection _owner;
    private readonly AiModelRowContent _content;

    internal AiModelRow(AiSettingsSection owner, AiUiProvider provider, AiUiModel model)
    {
        _owner = owner;
        Provider = provider;
        Model = model;
        _content = new(this);
    }

    private partial AiUiModel Model { get; set; }
    private partial AiUiProvider Provider { get; set; }

    internal void Update(AiUiProvider provider, AiUiModel model)
    {
        Provider = provider;
        Model = model;
    }

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is AiModelRow row)
        {
            Model = row.Model;
            Provider = row.Provider;
        }
    }

    internal AiUiModel CurrentModel => Model;
    internal partial bool Hovered { get; set; }
    internal partial bool Focused { get; set; }

    private Ui Body => new LauncherSettingsCard(CurrentModel.Name, _content, _owner.Window, groupedLeadingInset: AiBrandArtwork.TextInset);
    internal string Protocol => Provider.Protocol;

    internal void Edit() => _owner.OpenModel(Provider, CurrentModel);

    internal void Remove() => _ = _owner.State.RemoveModelAsync(Provider.Id, CurrentModel.Id);
}

internal sealed partial class AiModelRowContent(AiModelRow row) : Control
{
    private Ui Body => HoverArea(
        Grid(
            [GridLength.Star(), GridLength.Auto],
            [GridLength.Auto],
            [
                Column([
                    Text(row.CurrentModel.Name)
                        .FontSize(14f).Foreground(LauncherTheme.Ink)
                        .Wrap()
                        .Automation(new() { AutomationId = "ai.model.name." + row.CurrentModel.Id }),
                    Text(row.CurrentModel.Code)
                        .FontSize(12f).Foreground(LauncherTheme.Muted)
                        .Trim(TextTrimming.CharacterEllipsis).TooltipWhenTrimmed()
                        .Visible(row.Protocol == AiProtocols.RodelInference || row.CurrentModel.Name != row.CurrentModel.Code)
                        .Automation(new() { AutomationId = "ai.model.path." + row.CurrentModel.Id }),
                ])
                    .Spacing(4f)
                    .VAlign(VerticalAlignment.Center)
                    .Cell(0, 0),
                Opacity(Row([
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("edit", 16f)
                            ]),
                            row.Edit)
                            .Style(LauncherButtons.PopupCloseButton)
                            .Width(30f).Height(30f)
                            .ToolTip(Resources.AiEditModel)
                            .OnGotFocus(_ => row.Focused = true)
                            .OnLostFocus(() => row.Focused = false)
                            .Automation(new()
                            {
                                Name = Resources.AiEditModel,
                                AutomationId = "ai.model.edit." + row.CurrentModel.Id
                            }),
                        LauncherControls.OutlineCommand(
                            Row([
                                LauncherIcons.Line("delete", 16f, LauncherTheme.Error)
                            ]),
                            row.Remove)
                            .Style(LauncherButtons.PopupCloseButton)
                            .Width(30f).Height(30f)
                            .ToolTip(Resources.AiRemove)
                            .OnGotFocus(_ => row.Focused = true)
                            .OnLostFocus(() => row.Focused = false)
                            .Automation(new()
                            {
                                Name = Resources.AiRemove,
                                AutomationId = "ai.model.remove." + row.CurrentModel.Id
                            }),
                    ])
                        .Spacing(4f))
                    .Opacity(row.Hovered || row.Focused ? 1f : 0f)
                    .Cell(1, 0),
            ])
            .ColumnSpacing(12f),
        value => row.Hovered = value);
}

[View]
internal readonly partial record struct AiModelLogo(LauncherAppearance Theme, ImageSource? Source, string ModelId)
{
    /// <summary>Shows only a catalog-attributed model author logo, without labeling a proxy as the author.</summary>
    public Ui Body => Image(Source ?? Features.Instances.InstanceIconView.Transparent)
        .Stretch(ImageStretch.Uniform)
        .AlphaTint(Theme.Ink)
        .Width(AiBrandArtwork.Size).Height(AiBrandArtwork.Size)
        .VAlign(VerticalAlignment.Center)
        .Visible(Source is not null)
        .Automation(new() { AutomationId = "ai.model.logo." + ModelId });
}