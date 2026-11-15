using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Protects retained result snapshots and continuation behavior; typed input mapping is covered separately.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ResultNavigationTests
{
    /// <summary>
    /// Inventory queries need package identity and actionable warnings, not complete embedded author/release documents.
    /// Preserve raw unknown metadata through exact snapshot navigation; image and arbitrary text cases do not cover this projection.
    /// </summary>
    [TestMethod]
    public async Task ContentList_SummarizesMetadata_RetainsIdentityAndOriginalDetails()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("instance", "create", "Inventory", "--version", "synthetic-version")).ReadEnvelope(0);
        var id = created["data"]!["id"]!.GetValue<string>();
        var mods = Path.Combine(created["data"]!["gameDirectory"]!.GetValue<string>(), "mods");
        Directory.CreateDirectory(mods);
        var source = new JsonObject
        {
            ["schemaVersion"] = 1,
            ["id"] = "fixture",
            ["name"] = "Fixture",
            ["version"] = "1.2.3",
            ["depends"] = new JsonObject { ["minecraft"] = ">=1.21" },
            ["unknown"] = new string('a', 32000),
            ["significant"] = "  retained\n"
        };
        using (var archive = ZipFile.Open(Path.Combine(mods, "fixture.jar"), ZipArchiveMode.Create))
        await using (var writer = new StreamWriter(archive.CreateEntry("fabric.mod.json").Open()))
            await writer.WriteAsync(source.ToJsonString());
        await using var mcp = await McpSession.ConnectAsync(cli);

        var listed = await mcp.CallAsync("content_list", new JsonObject { ["instanceId"] = id });
        Assert.IsNull(listed["data"]!["truncated"]);
        Assert.IsFalse(listed["data"]!["hasMore"]!.GetValue<bool>());
        Assert.IsLessThan(5000, listed.ToJsonString().Length);
        var item = listed["data"]!["items"]!.AsArray().Single()!;
        Assert.AreEqual("fixture.jar", item["fileName"]!.GetValue<string>());
        Assert.AreEqual("Fixture", item["metadata"]!["name"]!.GetValue<string>());
        Assert.IsTrue(JsonNode.DeepEquals(source["depends"], item["metadata"]!["depends"]));
        Assert.IsNull(item["metadata"]!["unknown"]);
        var reference = listed["data"]!["fullResult"]!["continuation"]!["arguments"]!.DeepClone().AsObject();
        reference["pointer"] = "/data/items/0/metadata";
        var text = await ResultReader.ReadTextAsync(mcp,
            new JsonObject { ["tool"] = "result_read", ["arguments"] = reference }, TestContext.CancellationToken);
        Assert.IsTrue(JsonNode.DeepEquals(source, JsonNode.Parse(text)), "Default cost reduction must not discard upstream metadata.");
    }

    /// <summary>
    /// Default reads must finish without streaming embedded pictures. Explicit image reads must retain the original
    /// bytes after a live edit. Existing large-text cases do not exercise binary cost or MIME/JSON Pointer transport.
    /// </summary>
    [TestMethod]
    public async Task EmbeddedImages_DefaultAndSavedReadsStaySmall_ExplicitReadPreservesSnapshot()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("instance", "create", "With icon", "--version", "synthetic-version")).ReadEnvelope(0);
        var id = created["data"]!["id"]!.GetValue<string>();
        var metadataPath = Path.Combine(cli.Root, "instances", id, "instance.json");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(metadataPath, TestContext.CancellationToken))!.AsObject();
        // This is a binary transport fixture, not a PNG-decoder test; padding makes the former text leak observable.
        var image = new byte[96000];
        new Random(7319).NextBytes(image);
        Convert.FromHexString("89504E470D0A1A0A0000000D49484452").CopyTo(image, 0);
        var encoded = Convert.ToBase64String(image);
        metadata["icon"] = new JsonObject { ["format"] = "png", ["data"] = encoded };
        metadata["art/a~b"] = new JsonObject { ["picture"] = "data:image/png;base64," + encoded, ["label"] = "  retained label\n" };
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString(), TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);

        var listed = await mcp.CallAsync("instance_list", new JsonObject { ["limit"] = 100 });
        Assert.AreEqual("succeeded", listed["status"]?.GetValue<string>());
        Assert.IsFalse(listed["data"]!["hasMore"]!.GetValue<bool>());
        Assert.IsNull(listed["data"]!["continuation"]);
        Assert.IsNull(listed["data"]!["truncated"]);
        Assert.IsLessThan(6000, listed.ToJsonString().Length, "A small instance list must not become picture-text pages.");
        var item = listed["data"]!["items"]!.AsArray().Single()!;
        Assert.AreEqual(id, item["id"]?.GetValue<string>());
        Assert.IsNull(item["art/a~b"], "Unknown details belong in the optional snapshot, not every inventory response.");
        var icon = item["icon"]!["data"]!["continuation"]!["arguments"]!.AsObject();
        var detailReference = listed["data"]!["fullResult"]!["continuation"]!["arguments"]!.DeepClone().AsObject();
        detailReference["pointer"] = "/data/items/0/art~1a~0b";
        var details = await mcp.CallAsync("result_read", detailReference);
        var detail = JsonNode.Parse(details["data"]!["text"]!.GetValue<string>())!;
        Assert.AreEqual("  retained label\n", detail["label"]!.GetValue<string>());
        var uri = detail["picture"]!["continuation"]!["arguments"]!.AsObject();
        Assert.Contains("art~1a~0b", uri["pointer"]!.GetValue<string>());

        metadata["icon"] = null;
        metadata.Remove("art/a~b");
        await File.WriteAllTextAsync(metadataPath, metadata.ToJsonString(), TestContext.CancellationToken);
        CollectionAssert.AreEqual(image, await mcp.PreviewAsync(icon, "result_image_read"));
        CollectionAssert.AreEqual(image, await mcp.PreviewAsync(uri, "result_image_read"));
        var imageResult = await mcp.CallAsync("result_image_read", icon);
        Assert.AreEqual(image.Length, imageResult["data"]!["byteLength"]!.GetValue<int>());
        Assert.IsLessThan(1000, imageResult.ToJsonString().Length, "The image content block must not also appear as base64 JSON text.");

        var saved = await mcp.CallAsync("result_read", new JsonObject { ["resultId"] = icon["resultId"]!.DeepClone(), ["pointer"] = "/data" });
        Assert.IsFalse(saved["data"]!["hasMore"]!.GetValue<bool>(), "Old retained raw pictures must also be deferred during ordinary reads.");
        var read = JsonNode.Parse(saved["data"]!["text"]!.GetValue<string>())!;
        Assert.AreEqual("With icon", read["items"]![0]!["name"]!.GetValue<string>());
        Assert.AreEqual("result_image_read", read["items"]![0]!["icon"]!["data"]!["continuation"]!["tool"]!.GetValue<string>());
    }

    /// <summary>
    /// Output-budget page reduction must advance by returned items, preserving caller limits and every business value.
    /// Saved-array navigation already has coverage; this crosses the original live list's distinct continuation boundary.
    /// </summary>
    [TestMethod]
    public async Task LiveList_InlineBudgetReducesPage_ReturnsEveryItemWithoutSkipping()
    {
        using var cli = new CliSandbox();
        var expected = new HashSet<string>(StringComparer.Ordinal);
        var payload = new string('x', 3500);
        for (var index = 0; index < 8; index++)
        {
            var created = (await cli.RunAsync("instance", "create", "Entry " + index, "--version", "synthetic-version")).ReadEnvelope(0);
            var id = created["data"]!["id"]!.GetValue<string>();
            expected.Add(id);
            var path = Path.Combine(cli.Root, "instances", id, "instance.json");
            var metadata = JsonNode.Parse(await File.ReadAllTextAsync(path, TestContext.CancellationToken))!.AsObject();
            metadata["name"] = "Entry " + index + payload;
            await File.WriteAllTextAsync(path, metadata.ToJsonString(), TestContext.CancellationToken);
        }

        await using var mcp = await McpSession.ConnectAsync(cli);
        var arguments = new JsonObject { ["offset"] = 0, ["limit"] = 100 };
        var actual = new List<string>();
        var pages = 0;
        while (true)
        {
            Assert.IsLessThan(9, ++pages, "Pagination did not advance.");
            var page = await mcp.CallAsync("instance_list", arguments);
            Assert.IsNull(page["data"]!["truncated"], "A list should retain item pagination instead of becoming a JSON text stream.");
            Assert.IsLessThan(24000, page.ToJsonString().Length);
            foreach (var item in page["data"]!["items"]!.AsArray())
            {
                actual.Add(item!["id"]!.GetValue<string>());
                Assert.EndsWith(payload, item["name"]!.GetValue<string>());
            }

            if (page["data"]!["continuation"] is not { } continuation)
                break;
            Assert.AreEqual(100, continuation["arguments"]!["limit"]!.GetValue<int>());
            Assert.AreEqual(actual.Count, continuation["arguments"]!["offset"]!.GetValue<int>());
            arguments = continuation["arguments"]!.AsObject();
        }

        Assert.IsGreaterThan(1, pages, "The fixture must actually cross the output-budget page boundary.");
        CollectionAssert.AreEquivalent(expected.ToArray(), actual.ToArray());
    }

    /// <summary>
    /// Reading a truncated mutation result must return its saved snapshot, not rerun the command or read newer state.
    /// </summary>
    [TestMethod]
    public async Task OversizedMutation_FollowContinuation_ReturnsOriginalSnapshotWithoutReplay()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli);
        var original = new string('x', 28000) + " original snapshot";
        var bounded = await mcp.CallAsync("config_set", new JsonObject { ["key"] = "payload", ["value"] = original });
        Assert.AreEqual("succeeded", bounded["status"]?.GetValue<string>());
        Assert.IsTrue(bounded["data"]?["truncated"]?.GetValue<bool>());

        // Change the live value before reading: replaying the mutation would now overwrite this newer state.
        (await cli.RunAsync("config", "set", "payload", "newer-value")).ReadEnvelope(0);
        var settingsFile = Path.Combine(cli.Root, "settings.json");
        var liveBeforeRead = await File.ReadAllBytesAsync(settingsFile, TestContext.CancellationToken);

        var text = await ResultReader.ReadTextAsync(mcp, bounded["data"]!["continuation"]!.AsObject(), TestContext.CancellationToken);
        var saved = JsonNode.Parse(text)!;

        Assert.AreEqual("config_set", saved["operation"]?.GetValue<string>());
        Assert.AreEqual("succeeded", saved["status"]?.GetValue<string>());
        Assert.AreEqual(original, saved["data"]?["value"]?.GetValue<string>());
        CollectionAssert.AreEqual(liveBeforeRead, await File.ReadAllBytesAsync(settingsFile, TestContext.CancellationToken));
    }

    /// <summary>
    /// Paged arrays must preserve order and all elements when oversized items become references under an escaped JSON Pointer.
    /// </summary>
    [TestMethod]
    public async Task LargeArray_EscapedPointerAndItemReferences_ReconstructsEveryElementInOrder()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli);
        var expected = new JsonArray(
            new JsonObject { ["id"] = "first", ["payload"] = new string('a', 24000) },
            new JsonObject { ["id"] = "middle", ["payload"] = false },
            new JsonObject { ["id"] = "last", ["payload"] = new string('b', 24000) });
        var bounded = await mcp.CallAsync("config_set", new JsonObject
        {
            ["key"] = "payload",
            ["value"] = new JsonObject { ["a/b~c"] = expected.DeepClone() }
        });
        Assert.IsTrue(bounded["data"]?["truncated"]?.GetValue<bool>());
        var continuation = new JsonObject
        {
            ["tool"] = "result_read",
            ["arguments"] = new JsonObject
            {
                ["resultId"] = bounded["data"]!["resultId"]!.DeepClone(),
                ["pointer"] = "/data/value/a~1b~0c",
                ["limit"] = 2
            }
        };

        var reconstructed = await ResultReader.ReadArrayAsync(mcp, continuation, TestContext.CancellationToken);

        Assert.AreEqual(2, reconstructed.PageCount, "This fixture must cross an array-page boundary.");
        Assert.AreEqual(2, reconstructed.ReferenceCount, "Both oversized elements must be read through their references.");
        Assert.IsTrue(JsonNode.DeepEquals(expected, reconstructed.Items), "Paging or reference expansion changed array values or order.");
    }

    /// <summary>
    /// Invalid navigation must return an argument error without damaging the saved result or current settings.
    /// Rows cover pointer syntax, missing targets, and offsets beyond the retained text.
    /// </summary>
    [TestMethod]
    [DataRow("not-a-pointer", 0)]
    [DataRow("/data/missing", 0)]
    [DataRow("/data/value", 1000000)]
    public async Task ReadResult_InvalidNavigation_PreservesSavedAndLiveData(string pointer, int offset)
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli);
        var bounded = await mcp.CallAsync("config_set", new JsonObject { ["key"] = "payload", ["value"] = new string('x', 28000) });
        var resultId = bounded["data"]!["resultId"]!.GetValue<string>();
        var before = cli.SnapshotFiles();

        var failure = await mcp.CallAsync("result_read", new JsonObject { ["resultId"] = resultId, ["pointer"] = pointer, ["offset"] = offset });

        Assert.AreEqual("failed", failure["status"]?.GetValue<string>());
        Assert.AreEqual("INVALID_ARGUMENT", failure["error"]?["code"]?.GetValue<string>());
        var after = cli.SnapshotFiles();
        CollectionAssert.AreEquivalent(before.Keys.ToArray(), after.Keys.ToArray());
        foreach (var path in before.Keys)
            Assert.AreEqual(before[path], after[path], $"Navigation changed {path}.");
    }

    /// <summary>
    /// Failure to retain auxiliary output must not turn a completed mutation into a failure or erase its outcome.
    /// </summary>
    [TestMethod]
    public async Task OversizedMutation_ResultStorageUnavailable_PreservesSuccessAndActualWrite()
    {
        using var cli = new CliSandbox();
        await using var mcp = await McpSession.ConnectAsync(cli);
        var auxiliaryRoot = Path.Combine(cli.Root, "ai");
        Directory.CreateDirectory(auxiliaryRoot);
        await File.WriteAllTextAsync(Path.Combine(auxiliaryRoot, "tool-results"), "blocks the auxiliary directory", TestContext.CancellationToken);
        var expected = new string('x', 28000);

        var result = await mcp.CallAsync("config_set", new JsonObject { ["key"] = "payload", ["value"] = expected });
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken));

        Assert.AreEqual("succeeded", result["status"]?.GetValue<string>());
        Assert.IsNull(result["error"]);
        Assert.IsTrue(result["data"]?["truncated"]?.GetValue<bool>());
        Assert.IsNull(result["data"]?["resultId"]);
        Assert.IsNull(result["data"]?["continuation"]);
        Assert.Contains(warning => warning?.ToString().Contains("could not be saved", StringComparison.Ordinal) == true, result["warnings"]!.AsArray());
        Assert.AreEqual(expected, persisted?["payload"]?.GetValue<string>());
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}