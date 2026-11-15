// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Accounts;
using Rodel.MinecraftLauncher.Core.Application;
using Sprout.Graphics;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects the window's shared last-good avatar publication; real image sources stand in for completed decoding, without claiming network or rendering coverage.</summary>
[TestClass]
public sealed class AccountAvatarRetentionTests
{
    /// <summary>Review: authentication and scheduling tests do not protect images. A failed new download must leave the old image available to a newly opened menu; an old decode completing after a new success must not undo that success.</summary>
    [TestMethod]
    public void PendingOrFailedReplacement_RetainsLastImageAndRejectsLateOlderDecode()
    {
        var cache = new AccountAvatarImages();
        var account = Account();
        var old = ImageSource.FromPixels(new(1, 1, [255, 0, 0, 255]));
        var next = ImageSource.FromPixels(new(1, 1, [0, 255, 0, 255]));
        var oldCompletion = cache.Begin(account);
        oldCompletion(old);
        var changed = account with { SkinUrl = "https://skin.test/new.png" };
        var newCompletion = cache.Begin(changed);
        Assert.AreSame(old, cache.Get(changed)); // Failed decoding never publishes a replacement.
        newCompletion(next);
        oldCompletion(old);
        Assert.AreSame(next, cache.Get(changed));
    }

    /// <summary>Review: a provider switch or removal invalidates retained callbacks as well as lookups. These transitions must not leak an old role's image or reintroduce it through a late completion notification.</summary>
    [TestMethod]
    public void SourceChangeAndRemoval_InvalidateImagesAndOutstandingPublications()
    {
        var cache = new AccountAvatarImages();
        var account = Account();
        var source = ImageSource.FromPixels(new(1, 1, [0, 0, 255, 255]));
        var notifications = 0;
        cache.Available += (_, _) => notifications++;
        var original = cache.Begin(account);
        original(source);
        var rebound = account with { AuthServer = "https://second.test/api/" };
        Assert.IsNull(cache.Get(rebound));
        var pending = cache.Begin(rebound);
        original(source);
        Assert.AreEqual(1, notifications);
        Assert.IsNull(cache.Get(rebound));
        cache.Retain([]);
        pending(source);
        Assert.AreEqual(1, notifications);
        Assert.IsNull(cache.Get(rebound));
    }

    private static IdentityRecord Account() => new()
    {
        Id = "synthetic",
        Type = "external",
        Uuid = "0123456789abcdef0123456789abcdef",
        AuthServer = "https://first.test/api/",
        SkinUrl = "https://skin.test/old.png"
    };
}