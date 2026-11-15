// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Net;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects release catalog reuse, isolation and recovery through the real shared memory and disk boundaries.</summary>
[TestClass]
public sealed class VersionCatalogCacheTests
{
    /// <summary>Provides cancellation for isolated metadata operations.</summary>
    public TestContext TestContext { get; set; } = null!;

    // Author review: download-routing and seed-cache coverage cannot detect repeated launcher fetches.
    // A blocked HTTP producer establishes actual overlap; literal version IDs independently detect
    // request duplication, caller-mutation leaks and replacement. Only remote responses are substituted.
    /// <summary>Concurrent owners share one fetch, return independent snapshots and allow an explicit refresh.</summary>
    [TestMethod]
    public async Task ConcurrentOwners_ReuseOneFetch_WithoutSharingMutableResults()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            using var handler = new CatalogHandler();
            using var http = new HttpClient(handler);
            var first = new InstallationService(paths, http).GetVersionsAsync(cancellationToken: TestContext.CancellationToken);
            await handler.Entered.Task.WaitAsync(TestContext.CancellationToken);
            var second = new InstallationService(paths, http).GetVersionsAsync(cancellationToken: TestContext.CancellationToken);
            handler.Release.TrySetResult();
            var results = await Task.WhenAll(first, second);
            Assert.AreEqual(1, handler.Requests);
            results[0].Versions!.Clear();
            var reopened = await new InstallationService(paths, http).GetVersionsAsync(cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("1.21.1", reopened.Versions![0]!["id"]!.ToString());
            Assert.IsTrue(reopened.Cached);
            Assert.AreEqual(1, handler.Requests);
            var refreshed = await new InstallationService(paths, http).GetVersionsAsync(cancellationToken: TestContext.CancellationToken, refresh: true);
            Assert.AreEqual("26.3", refreshed.Versions![0]!["id"]!.ToString());
            Assert.AreEqual(2, handler.Requests);
        });
    }

    // Author review: real file age, failed network I/O and malformed bytes distinguish refresh and
    // recovery from simply returning an empty catalog. Existing download tests do not cross this cache.
    /// <summary>Expired/corrupt records refresh, while failed networking and cancellation preserve usable metadata.</summary>
    [TestMethod]
    public async Task DiskExpiryAndCorruption_RefreshWithoutDiscardingUsableContent()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var file = Path.Combine(paths.CacheDirectory, "version_manifest_v2.json");
            Directory.CreateDirectory(paths.CacheDirectory);
            await File.WriteAllTextAsync(file, "{\"versions\":[{\"id\":\"1.20.1\"}]}", TestContext.CancellationToken);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-1));
            using var handler = new CatalogHandler();
            handler.Release.TrySetResult();
            using var http = new HttpClient(handler);
            var service = new InstallationService(paths, http);
            var online = await service.GetVersionsAsync(cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("1.21.1", online.Versions![0]!["id"]!.ToString());
            handler.Fail = true;
            var fallback = await service.GetVersionsAsync(cancellationToken: TestContext.CancellationToken, refresh: true);
            Assert.IsTrue(fallback.Cached);
            Assert.AreEqual("1.21.1", fallback.Versions![0]!["id"]!.ToString());
            handler.Fail = false;
            await File.WriteAllTextAsync(file, "{broken", TestContext.CancellationToken);
            File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddMinutes(-1));
            var repaired = await service.GetVersionsAsync(cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("26.3", repaired.Versions![0]!["id"]!.ToString());
            using var canceled = new CancellationTokenSource();
            canceled.Cancel();
            await Assert.ThrowsAsync<OperationCanceledException>(() => service.GetVersionsAsync(cancellationToken: canceled.Token));
            var offline = await service.GetVersionsAsync(offline: true, cancellationToken: TestContext.CancellationToken);
            Assert.AreEqual("26.3", offline.Versions![0]!["id"]!.ToString());
        });
    }

    private sealed class CatalogHandler : HttpMessageHandler
    {
        internal TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Requests;
        internal bool Fail;

        /// <inheritdoc/>
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var count = Interlocked.Increment(ref Requests);
            Entered.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            if (Fail)
                throw new HttpRequestException("Catalog is unavailable.");
            return new(HttpStatusCode.OK) { Content = new StringContent("{\"versions\":[{\"id\":\"" + (count == 1 ? "1.21.1" : "26.3") + "\",\"type\":\"release\"}]}") };
        }
    }
}