// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Multiplayer;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Windows.Win32;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Checks stable executable identity through the real Windows filtering API without opening network listeners or changing firewall rules.</summary>
[TestClass]
public sealed class TerracottaRuntimeTests
{
    /// <summary>Provides cooperative cancellation for isolated file operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Review: identical bytes at different timestamp paths still cause distinct firewall prompts.
    /// The actual Windows app IDs, rather than copied-file hashes, must agree across two worker
    /// aliases. Deleting either alias must retain the common image, matching upstream cleanup.
    /// This test starts no network process and changes no machine security configuration.
    /// </summary>
    [TestMethod]
    public async Task DifferentWorkerAliases_HaveOneFirewallIdentity_AndKeepSharedImage()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var source = Path.Combine(paths.Root, "shipped.exe");
            var target = Path.Combine(paths.Root, "stable", "engine.exe");
            await File.WriteAllTextAsync(source, "verified image bytes", TestContext.CancellationToken);
            await TerracottaRuntime.CopyVerifiedAsync(source, target, TestContext.CancellationToken);
            await File.WriteAllTextAsync(target, "damaged cache", TestContext.CancellationToken);
            await TerracottaRuntime.CopyVerifiedAsync(source, target, TestContext.CancellationToken);
            Assert.AreEqual("verified image bytes", await File.ReadAllTextAsync(target, TestContext.CancellationToken));
            var first = Path.Combine(paths.Root, "timestamp-1", "embedded");
            var second = Path.Combine(paths.Root, "timestamp-2", "embedded");
            TerracottaRuntime.CreateJunction(first, Path.GetDirectoryName(target)!);
            TerracottaRuntime.CreateJunction(second, Path.GetDirectoryName(target)!);
            try
            {
                var stableId = FirewallIdentity(target);
                CollectionAssert.AreEqual(stableId, FirewallIdentity(Path.Combine(first, "engine.exe")));
                CollectionAssert.AreEqual(stableId, FirewallIdentity(Path.Combine(second, "engine.exe")));
            }
            finally
            {
                Directory.Delete(first);
                Directory.Delete(second);
            }

            Assert.AreEqual("verified image bytes", await File.ReadAllTextAsync(target, TestContext.CancellationToken));
        });
    }

    private static unsafe byte[] FirewallIdentity(string file)
    {
        var result = PInvoke.FwpmGetAppIdFromFileName0(file, out var id);
        Assert.AreEqual(0u, result);
        try { return new ReadOnlySpan<byte>(id->data, checked((int)id->size)).ToArray(); }
        finally { void* allocation = id; PInvoke.FwpmFreeMemory0(&allocation); }
    }
}