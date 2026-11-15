// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Create;

internal sealed partial class CreateCatalogTip : Control
{
    private readonly CreateController _controller;

    internal CreateCatalogTip(CreateController controller, CreateState state)
    {
        _controller = controller;
        State = state;
    }

    internal partial CreateState State { get; set; }

    private Ui Body => Column([
        Text(State.CatalogStatus)
            .FontSize(12f).Foreground(LauncherTheme.Ink)
            .Wrap()
            .Automation(new()
            {
                Name = State.CatalogStatus,
                AutomationId = "create.catalog-status"
            }),
        LauncherControls.OutlineCommand(Resources.CreateReload, () => _ = _controller.LoadVersionsAsync(refresh: true))
            .Enabled(!State.Loading && !State.RefreshingCatalog && !State.Active)
            .Automation(new()
            {
                Name = Resources.CreateReload,
                AutomationId = "create.reload"
            }),
    ])
        .Spacing(10f)
        .Width(320f);
}