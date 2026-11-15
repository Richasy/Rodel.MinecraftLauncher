// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Instances;

[View]
internal readonly partial record struct InstanceViewSegments(
    bool Servers,
    Action<int> Choose,
    string LocalLabel,
    string ServerLabel,
    LauncherSegmentedMaterial Material,
    string AutomationPrefix = "instances.view")
{
    public Ui Body => Column([
        LauncherControls.Segmented(new InstanceViewFilter(
                Servers,
                Choose,
                LocalLabel,
                ServerLabel,
                AutomationPrefix), Material)
            .HAlign(HorizontalAlignment.Stretch),
    ]);
}

internal sealed partial class InstanceViewFilter : Control
{
    internal InstanceViewFilter(
        bool servers,
        Action<int> choose,
        string localLabel,
        string serverLabel,
        string automationPrefix)
    {
        Servers = servers;
        Choose = choose;
        LocalLabel = localLabel;
        ServerLabel = serverLabel;
        AutomationPrefix = automationPrefix;
    }

    private partial bool Servers { get; set; }
    private partial Action<int> Choose { get; set; }
    private partial string LocalLabel { get; set; }
    private partial string ServerLabel { get; set; }
    private partial string AutomationPrefix { get; set; }
    private SegmentedItem[] Items => [new(LocalLabel)
        {
            Automation = new()
            {
                AutomationId = AutomationPrefix + ".local"
            }
        }, new(ServerLabel)
        {
            Automation = new()
            {
                AutomationId = AutomationPrefix + ".servers"
            }
        }, ];

    public override void ReconcileFrom(Control source)
    {
        if (source is InstanceViewFilter other)
        {
            Servers = other.Servers;
            Choose = other.Choose;
            LocalLabel = other.LocalLabel;
            ServerLabel = other.ServerLabel;
            AutomationPrefix = other.AutomationPrefix;
        }
    }

    private Ui Body => Segmented(Items)
        .SelectedIndex(Servers ? 1 : 0)
        .OnSelectionChanged(index => Choose(index))
        .Style(LauncherStyles.Segmented)
        .MinHeight(40f)
        .HAlign(HorizontalAlignment.Stretch)
        .Automation(new()
        {
            Name = Resources.InstancesView,
            AutomationId = AutomationPrefix
        });
}