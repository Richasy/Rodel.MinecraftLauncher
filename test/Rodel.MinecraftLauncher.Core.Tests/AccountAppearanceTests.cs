// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Accounts;
using Rodel.MinecraftLauncher.App.Features.Images;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects image ownership/commit recovery and crop geometry, without loading the App or replacing Core identity logic.</summary>
[TestClass]
public sealed class AccountAppearanceTests
{
    /// <summary>Checks that replacing/restoring one account's image preserves another account and copies the selected image.</summary>
    [TestMethod]
    public async Task AvatarCommit_ReopenAndRestore_PreservesOtherIdentity()
    {
        // Review: no existing test covers App-only avatar ownership. Real files and a re-opened store
        // distinguish an owned copy from a source-path preference, and per-account keys from one global image.
        var directory = Path.Combine(Path.GetTempPath(), "rodel-avatar-test-" + Guid.NewGuid().ToString("N"));
        var values = new Dictionary<string, object>();
        try
        {
            var store = new AccountAvatarStore(values, directory, ["steve", "cat"]);
            store.SavePreset("second", "cat");
            var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVQIHWP4z8DwHwAFgAI/ScLbtAAAAABJRU5ErkJggg==");
            await store.SaveCustomAsync("first", png, CancellationToken.None);
            var reopened = new AccountAvatarStore(values, directory, ["steve", "cat"]);
            var path = reopened.FilePath(reopened.Read("first"));
            CollectionAssert.AreEqual(png, await File.ReadAllBytesAsync(path, TestContext.CancellationToken));
            Assert.AreEqual("cat", reopened.Read("second").Preset);
            reopened.SavePreset("first", "steve");
            Assert.IsFalse(File.Exists(path));
            Assert.AreEqual("steve", store.Read("first").Preset);
            Assert.AreEqual("cat", store.Read("second").Preset);
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Forces preference failure after publishing a new PNG, retaining old bytes and removing only the failed new copy.</summary>
    [TestMethod]
    public async Task AvatarCommit_PreferenceWriteFails_RetainsOldFileAndCanRetry()
    {
        // Review: this reaches the actual file-publication/value-commit boundary. The sole double is
        // the package value dictionary failing its setter, not filesystem writes or the store algorithm.
        var directory = Path.Combine(Path.GetTempPath(), "rodel-avatar-failure-" + Guid.NewGuid().ToString("N"));
        var values = new FailingValues();
        try
        {
            var store = new AccountAvatarStore(values, directory, ["steve", "cat"]);
            await store.SaveCustomAsync("first", [1, 2, 3], CancellationToken.None);
            var old = store.FilePath(store.Read("first"));
            values.Fail = true;
            await Assert.ThrowsAsync<IOException>(() => store.SaveCustomAsync("first", [4, 5, 6], CancellationToken.None));
            Assert.AreEqual(old, store.FilePath(store.Read("first")));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, await File.ReadAllBytesAsync(old, TestContext.CancellationToken));
            CollectionAssert.AreEquivalent(new[] { old }, Directory.GetFiles(directory));
            values.Fail = false;
            await store.SaveCustomAsync("first", [7, 8, 9], CancellationToken.None);
            Assert.IsFalse(File.Exists(old));
            CollectionAssert.AreEqual(new byte[] { 7, 8, 9 }, await File.ReadAllBytesAsync(store.FilePath(store.Read("first")), TestContext.CancellationToken));
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    /// <summary>Verifies independently known corners of centered, zoomed and edge-clamped crops in both orientations.</summary>
    [TestMethod]
    [DataRow(8, 4, 2, 0, 5, 3, 3, 1, 4, 2, 6, 2, 7, 3)]
    [DataRow(4, 8, 0, 2, 3, 5, 1, 3, 2, 4, 2, 6, 3, 7)]
    public void Crop_NonSquareSource_ZoomAndPanKeepKnownCorners(int width, int height, int x0, int y0, int x1, int y1,
        int zx0, int zy0, int zx1, int zy1, int px0, int py0, int px1, int py1)
    {
        // Review: the synthetic raster encodes source coordinates, so fixed fixture expectations detect
        // stretch instead of cover, forgotten zoom, wrong pan sign and transparent out-of-bounds output.
        var input = new byte[width * height * 4];
        for (var y = 0; y < height; y++)
        {
            for (var x = 0; x < width; x++)
            {
                var offset = ((y * width) + x) * 4;
                input[offset] = (byte)x; input[offset + 1] = (byte)y; input[offset + 3] = 255;
            }
        }

        var crop = new ImageCrop(width, height, input);
        Corners(crop.Render(), x0, y0, x1, y1);
        crop.SetZoom(2);
        Corners(crop.Render(), zx0, zy0, zx1, zy1);
        crop.Move(1000, 1000);
        Corners(crop.Render(), px0, py0, px1, py1);
    }
    private static void Corners(byte[] output, int x0, int y0, int x1, int y1)
    {
        Assert.AreEqual((byte)x0, output[0]); Assert.AreEqual((byte)y0, output[1]);
        Assert.AreEqual((byte)x1, output[^4]); Assert.AreEqual((byte)y1, output[^3]);
        Assert.AreEqual((byte)255, output[3]); Assert.AreEqual((byte)255, output[^1]);
    }
    private sealed class FailingValues : Dictionary<string, object>, IDictionary<string, object>
    {
        /// <summary>Controls a deterministic package-value failure after the image file has been written.</summary>
        public bool Fail { get; set; }
        /// <summary>Preserves dictionary reads while rejecting a selected commit.</summary>
        public new object this[string key]
        {
            get => base[key];
            set { if (Fail) throw new IOException("Synthetic preference failure."); base[key] = value; }
        }
    }

    /// <summary>Gets or sets cooperative cancellation for file reads.</summary>
    public TestContext TestContext { get; set; } = null!;
}