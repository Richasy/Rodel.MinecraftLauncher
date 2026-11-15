// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Buttons;
using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Controls.Reports;
using Rodel.MinecraftLauncher.App.Theming;
using Rodel.MinecraftLauncher.Core.Packs;
using Sprout;
using Sprout.Graphics;
using Sprout.Layout;
using Sprout.Reconcile;
using Sprout.Widgets;
using Sprout.WindowsAppSDK;
using System.Text.Json;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Packs;

[View]
internal readonly partial record struct PackOptionalRowView(string Path, PackImportController Controller)
{
    public Ui Body => new PackOptionalRow(Path, Controller);
}

internal sealed partial class PackOptionalRow : Control
{
    private partial string EntryPath { get; set; }
    private readonly PackImportController _controller;

    internal PackOptionalRow(string path, PackImportController controller)
    {
        EntryPath = path;
        _controller = controller;
        State = _controller.State;
    }

    private partial PackImportState State { get; set; }

    private Ui Body => Border(new LauncherCheckBox(
LauncherAppearance.Capture(), EntryPath,
            !State.Excluded.Contains(EntryPath),
            () => _controller.ToggleOptional(EntryPath),
            !State.Active && State.Outcome != "succeeded" && State.RetainedInstanceId.Length == 0,
            "pack.import.optional." + EntryPath))
        .Padding(0f, 6f);

    public override void ReconcileFrom(Control source)
    {
        if (source is PackOptionalRow other)
            EntryPath = other.EntryPath;
    }

    private void OnChanged(PackImportState state) => State = state;

    protected override IDisposable? OnMounted()
    {
        _controller.Changed += OnChanged;
        State = _controller.State;
        return null;
    }

    protected override void OnDispose()
    {
        _controller.Changed -= OnChanged;
        base.OnDispose();
    }
}