// Copyright (c) Richasy. All rights reserved.

using Sprout;
using Sprout.Graphics;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Controls.Graphics;

[View]
internal readonly partial record struct SteveAvatar
{
    public Ui Body => Canvas([
        Rectangle(new Color(183, 141, 100, 255))
            .Width(40f).Height(40f),
        Rectangle(new Color(83, 60, 41, 255))
            .Width(40f).Height(15f),
        Rectangle(new Color(229, 206, 176, 255))
            .Width(10f).Height(5f)
            .At(5f, 20f),
        Rectangle(new Color(229, 206, 176, 255))
            .Width(10f).Height(5f)
            .At(25f, 20f),
        Rectangle(new Color(108, 104, 147, 255))
            .Width(5f).Height(5f)
            .At(10f, 20f),
        Rectangle(new Color(108, 104, 147, 255))
            .Width(5f).Height(5f)
            .At(25f, 20f),
        Rectangle(new Color(118, 81, 52, 255))
            .Width(10f).Height(5f)
            .At(15f, 25f),
        Rectangle(new Color(118, 81, 52, 255))
            .Width(20f).Height(5f)
            .At(10f, 30f),
    ])
        .Width(40f).Height(40f);
}