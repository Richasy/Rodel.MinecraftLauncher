// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Ai;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Protects reading intent across streamed growth, programmatic positioning and a return to the bottom.</summary>
[TestClass]
public sealed class TranscriptFollowTests
{
    /// <summary>An upward input must disarm follow before animated scrolling starts; automatic range changes and bottom clamps cannot re-arm it. Existing transcript event tests do not exercise this UI policy.</summary>
    [TestMethod]
    public void ReadingHistory_StaysDetachedAcrossAutomaticGrowthAndClamping()
    {
        var follow = new AiTranscriptFollow();
        follow.Set(true);
        follow.Observe(1000f, 1000f, automatic: true);
        follow.Set(false);
        follow.Observe(1000f, 1000f, automatic: true);
        Assert.IsFalse(follow.Following);
        follow.Observe(980f, 1200f, automatic: false);
        follow.Observe(980f, 2000f, automatic: true);
        follow.Observe(900f, 900f, automatic: true);
        Assert.IsFalse(follow.Following);
        follow.Observe(800f, 900f, automatic: false);
        follow.Observe(900f, 900f, automatic: false);
        Assert.IsTrue(follow.Following);
        follow.Observe(899f, 900f, automatic: false);
        Assert.IsFalse(follow.Following);
    }
}