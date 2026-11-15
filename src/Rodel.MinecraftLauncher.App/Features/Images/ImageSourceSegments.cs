// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Controls;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Images;

/// <summary>Uses the launcher's shared segmented material for the two image sources.</summary>
[View]
internal readonly partial record struct ImageSourceSegments(
    LauncherAppearance Theme,
    bool Custom,
    Action<int> Choose,
    string PresetLabel,
    bool Enabled)
{
    /// <summary>Wraps native mutually exclusive segments in the shared track and selection surface.</summary>
    public Ui Body => Column([
        LauncherControls.Segmented(new ImageSourceFilter(Custom, Choose, PresetLabel, Enabled), Theme.Materials.SegmentedMaterial)
            .HAlign(HorizontalAlignment.Stretch),
    ]);
}

/// <summary>Retains native segment selection as the chooser draft switches between image sources.</summary>
internal sealed partial class ImageSourceFilter : Control
{
    internal ImageSourceFilter(
        bool custom,
        Action<int> choose,
        string presetLabel,
        bool enabled)
    {
        Custom = custom;
        Choose = choose;
        PresetLabel = presetLabel;
        Enabled = enabled;
    }

    private partial bool Custom { get; set; }
    private partial Action<int> Choose { get; set; }
    private partial string PresetLabel { get; set; }
    private partial bool Enabled { get; set; }
    private SegmentedItem[] Items => [new(PresetLabel)
        {
            Automation = new()
            {
                AutomationId = "image.tab.presets"
            }
        }, new(Resources.ImageCustomTab)
        {
            Automation = new()
            {
                AutomationId = "image.tab.custom"
            }
        }, ];

    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
        if (source is ImageSourceFilter other)
        {
            Custom = other.Custom;
            Choose = other.Choose;
            PresetLabel = other.PresetLabel;
            Enabled = other.Enabled;
        }
    }

    private Ui Body => Segmented(Items)
        .SelectedIndex(Custom ? 1 : 0)
        .OnSelectionChanged(index => Choose(index))
        .Style(LauncherStyles.Segmented)
        .MinHeight(40f)
        .HAlign(HorizontalAlignment.Stretch)
        .Enabled(Enabled)
        .Automation(new()
        {
            Name = PresetLabel,
            AutomationId = "image.source"
        });
}