// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects original-symbol preparation across the real download lease, integrity check and durable cache.</summary>
[TestClass]
public sealed class SeedMappingCacheTests
{
    /// <summary>Provides cancellation for owned temporary mapping files.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A cold cache must acquire the downloader's actual destination lease once, publish verified bytes,
    /// repair corruption and subsequently reopen offline. Existing catalog tests substitute the producer
    /// and cannot detect a nested download lease or an unverified symbol file. Only HTTP is substituted;
    /// bounded cancellation makes the original self-deadlock fail instead of hanging the test process.
    /// </summary>
    [TestMethod]
    public async Task ColdMappings_PublishRepairAndReopenVerifiedCache()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            var bytes = Encoding.UTF8.GetBytes("# independent synthetic symbols\nexample.Client -> c:\n    double x -> a\n");
            var digest = Convert.ToHexStringLower(SHA1.HashData(bytes));
            var metadata = new JsonObject
            {
                ["downloads"] = new JsonObject
                {
                    ["client_mappings"] = new JsonObject { ["url"] = "https://example.invalid/client.txt", ["sha1"] = digest, ["size"] = bytes.LongLength },
                },
            };
            using var handler = new MappingResponse(bytes);
            using var client = new HttpClient(handler);
            var path = await SeedRuntimeBinding.ReadMappingsAsync(paths, metadata, deadline.Token, client);
            Assert.IsNotNull(path);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path, deadline.Token));
            Assert.AreEqual(1, handler.Requests);

            await File.WriteAllTextAsync(path, "corrupt symbols", deadline.Token);
            var repaired = await SeedRuntimeBinding.ReadMappingsAsync(paths, metadata, deadline.Token, client);
            Assert.AreEqual(path, repaired);
            CollectionAssert.AreEqual(bytes, await File.ReadAllBytesAsync(path, deadline.Token));
            Assert.AreEqual(2, handler.Requests);

            handler.Offline = true;
            Assert.AreEqual(path, await SeedRuntimeBinding.ReadMappingsAsync(paths, metadata, deadline.Token, client));
            Assert.AreEqual(2, handler.Requests, "Verified immutable symbols must remain usable without an HTTP request.");
        });
    }

    private sealed class MappingResponse(byte[] bytes) : HttpMessageHandler
    {
        internal int Requests { get; private set; }
        internal bool Offline { get; set; }

        /// <inheritdoc/>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            if (Offline) throw new HttpRequestException("The mapping source is offline.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) });
        }
    }
}