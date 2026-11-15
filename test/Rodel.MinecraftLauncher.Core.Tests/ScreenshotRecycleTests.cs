// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Screenshots;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Runtime.Versioning;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Crosses the real Windows shell recycle callback with owned files, independently of CLI parsing and packaged dialogs.</summary>
[TestClass]
[SupportedOSPlatform("windows")]
public sealed class ScreenshotRecycleTests
{
    /// <summary>
    /// Review: disappearing from the source directory is not sufficient evidence of recycling. The returned shell receipt
    /// must point to the exact recoverable bytes. Cleanup restores only this case's file and removes its corresponding bin metadata.
    /// </summary>
    [TestMethod]
    public async Task Recycle_ActualShellReceipt_PreservesRecoverableBytes()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("screenshots", "synthetic", TestContext.CancellationToken);
            var service = new ScreenshotService(paths);
            var source = Path.Combine(service.EnsureDirectory(instance.Id), "owned-" + Guid.NewGuid().ToString("N") + ".png");
            byte[] expected = [17, 29, 43];
            await File.WriteAllBytesAsync(source, expected, TestContext.CancellationToken);
            string? recycled = null;
            try
            {
                var result = await service.RecycleAsync(instance.Id, [service.GetFile(instance.Id, Path.GetFileName(source))], TestContext.CancellationToken);
                recycled = result.Items.Single().Destination;
                Assert.IsTrue(result.Succeeded, result.Items.Single().Error);
                Assert.IsNotNull(recycled);
                Assert.IsFalse(File.Exists(source));
                CollectionAssert.AreEqual(expected, await File.ReadAllBytesAsync(recycled, TestContext.CancellationToken));
            }
            finally
            {
                if (recycled is not null && File.Exists(recycled))
                {
                    File.Move(recycled, source);
                    var name = Path.GetFileName(recycled);
                    if (name.StartsWith("$R", StringComparison.Ordinal) && recycled.Contains("$Recycle.Bin", StringComparison.OrdinalIgnoreCase))
                        File.Delete(Path.Combine(Path.GetDirectoryName(recycled)!, "$I" + name[2..]));
                }
            }
        });
    }

    /// <summary>
    /// Review: validation must run inside the shell's pre-delete callback, not just before queuing. A controlled refusal
    /// at that real callback boundary must reach the caller unchanged and leave the original bytes intact.
    /// </summary>
    [TestMethod]
    public async Task Recycle_PreDeleteRefusal_PreservesSourceAndDoesNotFallback()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "owned-" + Guid.NewGuid().ToString("N") + ".png");
            await File.WriteAllBytesAsync(source, [7, 11, 13], TestContext.CancellationToken);
            var reached = false;
            var refusal = new IOException("Controlled selection revocation at the native effect boundary.");
            var error = await Assert.ThrowsAsync<IOException>(() => WindowsRecycleBin.RecycleAsync(source, () =>
            {
                reached = true;
                throw refusal;
            }, TestContext.CancellationToken));
            Assert.IsTrue(reached, "The native pre-delete callback must actually have run.");
            Assert.AreSame(refusal, error);
            CollectionAssert.AreEqual(new byte[] { 7, 11, 13 }, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        });
    }

    /// <summary>Gets the runner cancellation signal for owned shell operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}