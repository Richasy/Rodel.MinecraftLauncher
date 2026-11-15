using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Packs;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects online pack release selection and channel-isolated caches, independently of file application and rollback.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class OnlinePackSelectionTests
{
    /// <summary>Provides the runner's cancellation signal for the controlled provider requests.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// A later prerelease must not replace a requested stable release or contaminate its offline selection.
    /// The existing frozen-apply tests begin after selection and cannot detect this wrong-release boundary.
    /// Exact version choices remain usable outside a suggested channel rather than becoming a new hard gate.
    /// A real loopback provider controls publication order; stopping it establishes actual cached recovery.
    /// </summary>
    [TestMethod]
    public async Task Select_ChannelCachesAndExactOverride_DoNotDriftToAnotherRelease()
    {
        using var sandbox = new CliSandbox();
        await using var server = new PackHttpServer();
        server.Set("project/pack", """{"id":"pack","project_type":"modpack","title":"Selection fixture"}""");
        server.Set("project/pack/version", new JsonArray(
            Version("alpha-newest", "alpha", "2026-03-01T00:00:00Z"),
            Version("beta-middle", "beta", "2026-02-01T00:00:00Z"),
            Version("stable-oldest", "release", "2026-01-01T00:00:00Z")).ToJsonString());
        using var http = new HttpClient();
        var paths = new LauncherPaths(sandbox.Root);
        var packs = new OnlinePackService(paths, new ContentPlatformService(paths, http, server.Root));

        var stable = await packs.SelectAsync("pack", channel: "release", ct: TestContext.CancellationToken);
        var beta = await packs.SelectAsync("pack", channel: "beta", ct: TestContext.CancellationToken);
        var unrestricted = await packs.SelectAsync("pack", ct: TestContext.CancellationToken);
        var explicitBeta = await packs.SelectAsync("pack", "beta-middle", channel: "release", ct: TestContext.CancellationToken);

        Assert.AreEqual("stable-oldest", stable.VersionId);
        Assert.AreEqual("beta-middle", beta.VersionId);
        Assert.AreEqual("alpha-newest", unrestricted.VersionId);
        Assert.AreEqual("beta-middle", explicitBeta.VersionId);
        Assert.AreEqual("explicit-version-channel-override", explicitBeta.SelectionWarnings?[0].Code);
        await server.DisposeAsync();

        Assert.AreEqual("stable-oldest", (await packs.SelectAsync("pack", offline: true, channel: "release", ct: TestContext.CancellationToken)).VersionId);
        Assert.AreEqual("beta-middle", (await packs.SelectAsync("pack", offline: true, channel: "beta", ct: TestContext.CancellationToken)).VersionId);
        Assert.AreEqual("alpha-newest", (await packs.SelectAsync("pack", offline: true, ct: TestContext.CancellationToken)).VersionId);
    }

    private static JsonObject Version(string id, string type, string published) => new()
    {
        ["id"] = id,
        ["version_number"] = id,
        ["version_type"] = type,
        ["date_published"] = published,
        ["files"] = new JsonArray(new JsonObject
        {
            ["filename"] = id + ".mrpack",
            ["primary"] = true,
            ["url"] = "https://example.invalid/" + id + ".mrpack",
            ["size"] = 1
        })
    };
}