// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects screenshot discovery and selected-file ownership without depending on image decoding, worlds or a running game.</summary>
[TestClass]
public sealed class ScreenshotFileTests
{
    /// <summary>
    /// Review: opening an unused gallery must not create data. Mixed folders must retain ordinary images, including an
    /// incomplete image that the UI can report, without reading saves or treating unrelated files and directories as screenshots.
    /// Existing world/content inventories do not exercise this independent directory.
    /// </summary>
    [TestMethod]
    public async Task Inventory_MissingThenMixedDirectory_PreservesFilesAndUsesActualInstance()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var empty = await service.ListAsync(instance.Id, TestContext.CancellationToken);
            Assert.IsFalse(empty.Exists);
            Assert.IsFalse(Directory.Exists(empty.Directory));

            var directory = service.EnsureDirectory(instance.Id);
            var old = Path.Combine(directory, "old.PNG");
            var recent = Path.Combine(directory, "recent.jpg");
            await File.WriteAllBytesAsync(old, [1, 2, 3], TestContext.CancellationToken);
            await File.WriteAllBytesAsync(recent, [], TestContext.CancellationToken);
            await File.WriteAllTextAsync(Path.Combine(directory, "notes.txt"), "keep", TestContext.CancellationToken);
            Directory.CreateDirectory(Path.Combine(directory, "not-an-image.png"));
            File.SetLastWriteTimeUtc(old, new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetLastWriteTimeUtc(recent, new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc));
            File.SetAttributes(old, FileAttributes.ReadOnly);
            try
            {
                var catalog = await service.ListAsync(instance.Id, TestContext.CancellationToken);
                CollectionAssert.AreEqual(expected, catalog.Files.Select(file => file.FileName).ToArray());
                Assert.AreEqual(0L, catalog.Files[0].Bytes, "A still-writing image stays discoverable; decoding is a separate concern.");
                Assert.IsEmpty(catalog.Warnings);
                Assert.AreEqual("keep", await File.ReadAllTextAsync(Path.Combine(directory, "notes.txt"), TestContext.CancellationToken));
                Assert.IsTrue(Directory.Exists(Path.Combine(directory, "not-an-image.png")));
                Assert.IsFalse(Directory.Exists(Path.Combine(instance.GameDirectory, "saves")));
            }
            finally { File.SetAttributes(old, FileAttributes.Normal); }
        });
    }

    /// <summary>
    /// Review: a selected image may change or belong to another instance, and a requested name may collide or escape.
    /// Each refusal must preserve both real files; a metadata-only success assertion would miss accidental overwrite.
    /// </summary>
    [TestMethod]
    [DataRow("collision")]
    [DataRow("escape")]
    [DataRow("foreign")]
    [DataRow("changed")]
    public async Task Rename_InvalidSelectionOrTarget_PreservesEveryFile(string failure)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var store = new InstanceStore(paths);
            var instance = await store.CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var directory = service.EnsureDirectory(instance.Id);
            var source = Path.Combine(directory, "source.png");
            var occupied = Path.Combine(directory, "occupied.png");
            await File.WriteAllBytesAsync(source, [1, 2, 3], TestContext.CancellationToken);
            await File.WriteAllBytesAsync(occupied, [4, 5, 6], TestContext.CancellationToken);
            var selected = service.GetFile(instance.Id, "source.png");
            var name = failure == "escape" ? "..\\escaped.png" : failure == "collision" ? "occupied.png" : "renamed.png";
            if (failure == "foreign") selected = selected with { Path = Path.Combine(paths.Root, "elsewhere", "source.png") };
            if (failure == "changed") File.SetLastWriteTimeUtc(source, selected.ModifiedUtc.UtcDateTime.AddMinutes(1));
            var error = await Assert.ThrowsAsync<Exception>(() => service.RenameAsync(instance.Id, selected, name, TestContext.CancellationToken));
            if (failure is "foreign" or "changed") Assert.AreEqual("SCREENSHOT_CHANGED", ((LauncherOperationException)error).Code);
            else if (failure == "collision") Assert.AreEqual("SCREENSHOT_DESTINATION_EXISTS", ((LauncherOperationException)error).Code);
            else Assert.IsInstanceOfType<ArgumentException>(error);
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
            CollectionAssert.AreEqual(new byte[] { 4, 5, 6 }, await File.ReadAllBytesAsync(occupied, TestContext.CancellationToken));
            CollectionAssert.AreEquivalent(new[] { source, occupied }, Directory.GetFiles(directory));
            Assert.IsFalse(File.Exists(Path.Combine(instance.GameDirectory, "escaped.png")));
        });
    }

    /// <summary>Review: Unicode and case-only renames must change the actual filename while preserving bytes and the original format, not silently report an unchanged name.</summary>
    [TestMethod]
    public async Task Rename_UnicodeThenCaseOnly_PreservesImageBytes()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var directory = service.EnsureDirectory(instance.Id);
            await File.WriteAllBytesAsync(Path.Combine(directory, "original.png"), [9, 8, 7], TestContext.CancellationToken);
            var renamed = await service.RenameAsync(instance.Id, service.GetFile(instance.Id, "original.png"), "湖边 日落", TestContext.CancellationToken);
            Assert.AreEqual("湖边 日落.png", Path.GetFileName(Directory.GetFiles(directory).Single()));
            renamed = await service.RenameAsync(instance.Id, renamed, "湖边 日落.PNG", TestContext.CancellationToken);
            Assert.AreEqual("湖边 日落.PNG", Path.GetFileName(Directory.GetFiles(directory).Single()));
            CollectionAssert.AreEqual(new byte[] { 9, 8, 7 }, await File.ReadAllBytesAsync(renamed.Path, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Review: a batch must retain an earlier published copy and report later collisions and changed selections independently.
    /// Comparing source and destination bytes detects accidental moves, overwrites and implicit copying of unselected files.
    /// </summary>
    [TestMethod]
    public async Task Export_PartialFailure_PreservesSourcesDestinationsAndActualReceipts()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var directory = service.EnsureDirectory(instance.Id);
            var output = Path.Combine(paths.Root, "exported");
            Directory.CreateDirectory(output);
            string[] names = ["first.png", "occupied.png", "changed.png", "unselected.png"];
            byte[] original = [1, 3, 5];
            foreach (var name in names)
                await File.WriteAllBytesAsync(Path.Combine(directory, name), original, TestContext.CancellationToken);
            var files = names.Take(3).Select(name => service.GetFile(instance.Id, name)).ToArray();
            await File.WriteAllBytesAsync(Path.Combine(output, "occupied.png"), [8, 6, 4], TestContext.CancellationToken);
            File.SetLastWriteTimeUtc(files[2].Path, files[2].ModifiedUtc.UtcDateTime.AddMinutes(1));
            var result = await service.ExportAsync(instance.Id, files, output, TestContext.CancellationToken);
            Assert.IsFalse(result.Succeeded);
            Assert.AreEqual(1, result.Completed);
            Assert.HasCount(3, result.Items);
            Assert.AreEqual("SCREENSHOT_DESTINATION_EXISTS", result.Items[1].ErrorCode);
            Assert.AreEqual("SCREENSHOT_CHANGED", result.Items[2].ErrorCode);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(result.Items[0].Destination!, TestContext.CancellationToken));
            CollectionAssert.AreEqual(new byte[] { 8, 6, 4 }, await File.ReadAllBytesAsync(Path.Combine(output, "occupied.png"), TestContext.CancellationToken));
            Assert.HasCount(2, Directory.GetFiles(output));
            Assert.HasCount(4, Directory.GetFiles(directory));
            foreach (var source in Directory.GetFiles(directory))
                CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        });
    }

    /// <summary>
    /// Review: the irreversible path must still honor cancellation and exact selections, not delete changed or unselected files.
    /// This uses real isolated files and checks partial effects; existing export/recycle cases do not exercise permanent removal.
    /// </summary>
    [TestMethod]
    public async Task Delete_CanceledThenPartial_PreservesChangedAndUnselectedFiles()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var directory = service.EnsureDirectory(instance.Id);
            string[] names = ["delete.png", "changed.png", "unselected.png"];
            foreach (var name in names)
                await File.WriteAllBytesAsync(Path.Combine(directory, name), [1, 2, 3], TestContext.CancellationToken);
            var selected = names.Take(2).Select(name => service.GetFile(instance.Id, name)).ToArray();
            using var canceled = new CancellationTokenSource();
            await canceled.CancelAsync();
            var canceledResult = await service.DeleteAsync(instance.Id, selected, canceled.Token);
            Assert.IsTrue(canceledResult.Canceled);
            Assert.IsEmpty(canceledResult.Items);
            Assert.HasCount(3, Directory.GetFiles(directory));

            await File.WriteAllBytesAsync(selected[1].Path, [8, 9, 10, 11], TestContext.CancellationToken);
            var result = await service.DeleteAsync(instance.Id, selected, TestContext.CancellationToken);
            Assert.AreEqual(1, result.Completed);
            Assert.IsFalse(result.Succeeded);
            Assert.IsFalse(File.Exists(selected[0].Path));
            Assert.IsNull(result.Items[0].Destination);
            Assert.AreEqual("SCREENSHOT_CHANGED", result.Items[1].ErrorCode);
            CollectionAssert.AreEqual(new byte[] { 8, 9, 10, 11 }, await File.ReadAllBytesAsync(selected[1].Path, TestContext.CancellationToken));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(Path.Combine(directory, "unselected.png"), TestContext.CancellationToken));
        });
    }

    /// <summary>Gets the runner cancellation signal for owned fixture I/O.</summary>
    public TestContext TestContext { get; set; } = null!;

    private static readonly string[] expected = ["recent.jpg", "old.PNG"];
}