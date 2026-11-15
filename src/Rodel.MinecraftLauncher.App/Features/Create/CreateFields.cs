// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Controls.Versions;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed partial class CreateFields : Control
{
    private readonly CreateController _controller;
    private readonly Window _window;
    private Flyout? _info;
    private CreateCatalogTip? _tip;

    internal CreateFields(CreateController controller, Window window)
    {
        _controller = controller;
        _window = window;
        State = controller.State;
    }

    internal partial CreateState State { get; set; }
    private bool Editable => !State.Active && !State.Loading && State.InstanceId is null;

    private Ui Body => Column([
        LauncherControls.OutlineCommand(Resources.CreateAnother, _controller.StartNew)
            .Visible(State.InstanceId is not null && !State.Active)
            .Automation(new()
            {
                Name = Resources.CreateAnother,
                AutomationId = "create.another"
            }),
        Text(Resources.CreateRecoveryHeading)
            .FontSize(12f).Foreground(LauncherTheme.Ink)
            .Visible(State.Recoveries.Count > 0),
        LauncherControls.Choice(State.RecoveryNames)
            .SelectedIndex(State.Recoveries.ToList()
                .FindIndex(item => item.InstanceId == State.InstanceId))
            .OnSelectionChanged(index => _ = _controller.SelectRecoveryAsync(index))
            .MinHeight(40f)
            .HAlign(HorizontalAlignment.Stretch)
            .Enabled(!State.Active && !State.Loading)
            .Visible(State.Recoveries.Count > 0)
            .Automation(new()
            {
                Name = Resources.CreateRecoveryHeading,
                AutomationId = "create.recovery"
            }),
        Text(Resources.ImportName)
            .FontSize(12f).Foreground(LauncherTheme.Ink),
        TextBox(State.Name, _controller.ChangeName)
            .Enabled(Editable)
            .Automation(new()
            {
                Name = Resources.ImportName,
                AutomationId = "create.name"
            }),
        Row([
            Text(Resources.CreateVersion)
                .FontSize(12f).Foreground(LauncherTheme.Ink)
                .Weight(1f)
                .VAlign(VerticalAlignment.Center),
            LauncherControls.Command(
                Row([
                    LauncherIcons.Line("info", 18f)
                ]),
                ShowCatalogInfo)
                .Style(LauncherButtons.IconButton)
                .Width(28f).Height(28f)
                .Automation(new()
                {
                    Name = Resources.CreateCatalogInfo,
                    AutomationId = "create.catalog-info",
                    HelpText = State.CatalogStatus
                }),
        ])
            .Spacing(8f),
        GameVersionPicker.Create(State.Catalog, State.VersionText, _controller.ChangeVersionText, _controller.ValidateVersion,
            "create.version", Editable && !State.Loading, State.VersionInvalid).MinHeight(40f)
            .Automation(new() { Name = Resources.CreateVersion, AutomationId = "create.version" }),
        Text(State.VersionHint)
            .FontSize(12f).Foreground(LauncherTheme.Error)
            .Wrap()
            .Automation(new()
            {
                Name = State.VersionHint,
                AutomationId = "create.version-hint"
            })
            .Visible(State.VersionHint.Length > 0),
        new PathSummaryView(Resources.CreateDestination, _controller.Destination, "create.destination", Appearance: PathSummaryAppearance.Capture()),
    ])
        .Spacing(10f);

    internal void Update(CreateState state)
    {
        State = state;
        _tip?.State = state;
    }

    private void ShowCatalogInfo()
    {
        if (_info?.IsOpen == true)
        {
            _info.Hide();
            return;
        }

        _info = new Flyout(() => _tip = new CreateCatalogTip(_controller, State))
        {
            Placement = PopupPlacement.BottomEdgeAlignedRight,
            ShowMode = FlyoutShowMode.Transient,
        };
        _info.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(16f).MaxWidth(380f));
        _info.Automation(new()
        {
            Name = Resources.CreateCatalogInfo,
            AutomationId = "create.catalog-tip"
        });
        _info.ShowAt(_window);
    }

    protected override IDisposable? OnMounted()
    {
        _window.ActualThemeChanged += OnThemeChanged;
        return null;
    }

    private void OnThemeChanged(object? sender, EventArgs e) => _info?.LauncherStyle(popupAppearance => popupAppearance.Menus.ErrorPopup.Padding(16f).MaxWidth(380f));

    protected override void OnDispose()
    {
        _window.ActualThemeChanged -= OnThemeChanged;
        _info?.Hide();
        base.OnDispose();
    }
}