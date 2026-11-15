// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Controls.Graphics;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Servers;

[View]
internal readonly partial record struct ServerIconView(ImageSource? Source, float Size = 36f)
{
    public Ui Body => Overlay([
        LauncherIcons.Line("servers", Size)
            .Visible(Source is null),
        Image(Source ?? InstanceIconView.Transparent)
            .Stretch(ImageStretch.Uniform)
            .Visible(Source is not null),
    ])
        .Width(Size).Height(Size);
}