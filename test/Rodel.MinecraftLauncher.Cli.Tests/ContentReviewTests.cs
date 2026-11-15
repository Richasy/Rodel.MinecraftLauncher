namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Verifies frozen local review identity, preflight, and invalidation; direct content mutations have a separate suite.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ContentReviewTests
{
    /// <summary>
    /// A changed later source must reject the whole reviewed batch before the first item is copied; a new review can recover.
    /// </summary>
    [TestMethod]
    public async Task ApplyReview_LaterSourceChanged_RejectsWholeBatchUntilReviewedAgain()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var later = Path.Combine(Path.GetDirectoryName(fixture.Source)!, "later.jar");
        ContentFixture.WritePackage(later, "reviewed bytes");
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source, later)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(review["canApply"]?.GetValue<bool>());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "selected.jar")), "Review must not install files.");
        ContentFixture.WritePackage(later, "changed after review");
        var firstBytes = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);
        var laterBytes = await File.ReadAllBytesAsync(later, TestContext.CancellationToken);

        var failure = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_SOURCE_CHANGED", failure["error"]?["code"]?.GetValue<string>());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "selected.jar")), "Preflight must finish before copying any source.");
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "later.jar")));
        CollectionAssert.AreEqual(laterBytes, await File.ReadAllBytesAsync(later, TestContext.CancellationToken));

        // Only a fresh review authorizes the newly observed source bytes.
        var fresh = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source, later)).ReadEnvelope(0)["data"]!;
        Assert.AreNotEqual(review["id"]!.GetValue<string>(), fresh["id"]!.GetValue<string>());
        var applied = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, fresh["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(0);
        Assert.AreEqual(2, applied["data"]?["succeededCount"]?.GetValue<int>());
        CollectionAssert.AreEqual(firstBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, "selected.jar"), TestContext.CancellationToken));
        CollectionAssert.AreEqual(laterBytes, await File.ReadAllBytesAsync(Path.Combine(fixture.Mods, "later.jar"), TestContext.CancellationToken));
    }

    /// <summary>
    /// An intervening inventory edit must invalidate an otherwise unchanged source review and preserve the user's new file.
    /// </summary>
    [TestMethod]
    public async Task ApplyReview_InventoryChanged_RejectsStaleReviewAndPreservesNewContent()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source)).ReadEnvelope(0)["data"]!;
        var added = Path.Combine(fixture.Mods, "user-added.jar");
        ContentFixture.WritePackage(added, "intervening user content");
        var before = await File.ReadAllBytesAsync(added, TestContext.CancellationToken);

        var failure = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_REVIEW_STALE", failure["error"]?["code"]?.GetValue<string>());
        Assert.IsFalse(File.Exists(Path.Combine(fixture.Mods, "selected.jar")));
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(added, TestContext.CancellationToken));
        Assert.AreEqual("unselected", await File.ReadAllTextAsync(Path.Combine(fixture.Mods, "notes.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// A valid review cannot be applied to another instance even when both instances have compatible empty inventories.
    /// </summary>
    [TestMethod]
    public async Task ApplyReview_DifferentInstance_RejectsWithoutInstallingIntoEitherInstance()
    {
        using var cli = new CliSandbox();
        var first = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var second = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var review = (await cli.RunAsync("content", "review-files", first.InstanceId, first.Source)).ReadEnvelope(0)["data"]!;

        var failure = (await cli.RunAsync("content", "apply-review", second.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_REVIEW_MISMATCH", failure["error"]?["code"]?.GetValue<string>());
        Assert.IsFalse(File.Exists(Path.Combine(first.Mods, "selected.jar")));
        Assert.IsFalse(File.Exists(Path.Combine(second.Mods, "selected.jar")));
    }

    /// <summary>
    /// Explicit confirmation must not bypass conflicts that the review has already marked unresolved.
    /// </summary>
    [TestMethod]
    public async Task ApplyReview_UnresolvedNameConflict_PreservesExistingAndSourceBytes()
    {
        using var cli = new CliSandbox();
        var fixture = await ContentFixture.CreateAsync(cli, TestContext.CancellationToken);
        var target = Path.Combine(fixture.Mods, "selected.jar");
        ContentFixture.WritePackage(target, "different installed bytes");
        var original = await File.ReadAllBytesAsync(target, TestContext.CancellationToken);
        var source = await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken);
        var review = (await cli.RunAsync("content", "review-files", fixture.InstanceId, fixture.Source)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(review["canApply"]?.GetValue<bool>());

        var failure = (await cli.RunAsync("content", "apply-review", fixture.InstanceId, review["id"]!.GetValue<string>(), "--confirm")).ReadEnvelope(1);

        Assert.AreEqual("CONTENT_REVIEW_UNRESOLVED", failure["error"]?["code"]?.GetValue<string>());
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(target, TestContext.CancellationToken));
        CollectionAssert.AreEqual(source, await File.ReadAllBytesAsync(fixture.Source, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}