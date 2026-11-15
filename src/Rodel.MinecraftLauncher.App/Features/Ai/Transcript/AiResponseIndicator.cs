// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Theming;
using Sprout;
using Sprout.Animation;
using Sprout.Layout;
using Sprout.Reconcile;
using static Sprout.Markup;

namespace Rodel.MinecraftLauncher.App.Features.Ai;

/// <summary>Shows response activity above the composer without adding a synthetic transcript message.</summary>
[View]
internal readonly partial record struct AiResponseIndicator
{
    /// <summary>Creates the retained native animation host only while the selected conversation is responding.</summary>
    public Ui Body => new AiResponseDots().Automation(new()
    {
        Name = Resources.AiChatResponding,
        AutomationId = "ai.responding"
    });
}

internal sealed partial class AiResponseDots : Control
{
    /// <inheritdoc/>
    public override void ReconcileFrom(Control source)
    {
    }

    /// <summary>Builds three staggered dots; Sprout freezes nonessential motion under reduced-motion settings.</summary>
    private Ui Body => Row([
        Border()
            .Background(LauncherTheme.Muted)
            .CornerRadius(3f)
            .Width(5f).Height(5f)
            .Animation(ExpressionAnimation.TranslateY(-4f * MotionExpr.Max(0f, MotionExpr.Sin(MotionExpr.Time * 5.2f)))),
        Border()
            .Background(LauncherTheme.Muted)
            .CornerRadius(3f)
            .Width(5f).Height(5f)
            .Animation(ExpressionAnimation.TranslateY(-4f * MotionExpr.Max(0f, MotionExpr.Sin((MotionExpr.Time * 5.2f) - .8f)))),
        Border()
            .Background(LauncherTheme.Muted)
            .CornerRadius(3f)
            .Width(5f).Height(5f)
            .Animation(ExpressionAnimation.TranslateY(-4f * MotionExpr.Max(0f, MotionExpr.Sin((MotionExpr.Time * 5.2f) - 1.6f)))),
    ])
        .Spacing(5f)
        .Height(18f)
        .VAlign(VerticalAlignment.Center).HAlign(HorizontalAlignment.Left);
}