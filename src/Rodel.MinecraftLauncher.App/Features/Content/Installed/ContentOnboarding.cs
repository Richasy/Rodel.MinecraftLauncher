// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.App.Layout;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Popups;
using Sprout.Reconcile;
using Sprout.Styling;
using Sprout.Widgets;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Content;

internal sealed partial class ContentOnboarding(ContentPage page) : Control
{
    private readonly ContentOnboardingDetails _details = new(page);
    internal partial float ViewportHeight { get; set; }

    private Ui Body => Overlay([
        new Controls.LauncherMaterialSurface(LauncherMaterials.Acrylic).Margin(1f),
        Border(Grid(
            [GridLength.Star()],
            [GridLength.Star(), GridLength.Auto, GridLength.Star()],
            [
                _details.Automation(new() { AutomationId = "content.onboarding.group" })
                    .Cell(0, 1),
            ]))
        .BorderBrush(LauncherTheme.Edge)
        .BorderThickness(1f),
    ]).MinHeight(ViewportHeight);
}

internal sealed partial class ContentOnboardingDetails(ContentPage page) : Control
{
    private string Title => !page.Home.InstancesLoaded
        ? Resources.ContentLoading
        : page.NoInstances
        ? Resources.ContentNoInstancesHeading
        : page.Home.InstanceLoadError.Length > 0 ? Resources.ContentReadFailedHeading : Resources.MetadataUnreadable;
    private string Description => !page.Home.InstancesLoaded
        ? string.Empty
        : page.NoInstances
        ? Resources.ContentNoInstancesDescription
        : page.Home.InstanceLoadError.Length > 0 ? string.Format(Resources.InstancesReadError, page.Home.InstanceLoadError) : Resources.ContentNoReadableInstance;

    private Ui Body => Column([
        LauncherIcons.Line("instances", 48f, LauncherTheme.Muted)
            .HAlign(HorizontalAlignment.Center)
            .Automation(new() { AutomationId = "content.onboarding.icon" }),
        Text(Title)
            .FontSize(22f).FontWeight(600).Foreground(LauncherTheme.Ink)
            .Wrap()
            .Align(TextAlignment.Center)
            .Automation(new()
            {
                Name = Title,
                AutomationId = "content.onboarding.title"
            }),
        Text(Description)
            .FontSize(13f).Foreground(LauncherTheme.Muted)
            .Wrap()
            .Align(TextAlignment.Center)
            .Automation(new() { AutomationId = "content.onboarding.description" }),
        Row([
            LauncherControls.Command(Resources.ContentAddInstance, () => page.CreateInstance())
                .Visible(page.NoInstances)
                .Automation(new()
                {
                    Name = Resources.ContentAddInstance,
                    AutomationId = "content.empty.create"
                }),
            LauncherControls.OutlineCommand(Resources.NavInstances, () => page.ManageInstances())
                .Visible(page.Home.InstancesLoaded && !page.NoInstances)
                .Automation(new()
                {
                    Name = Resources.NavInstances,
                    AutomationId = "content.empty.manage"
                }),
        ])
            .Spacing(12f)
            .HAlign(HorizontalAlignment.Center)
            .Visible(page.Home.InstancesLoaded),
    ])
        .Spacing(18f)
        .Padding(28f);
}