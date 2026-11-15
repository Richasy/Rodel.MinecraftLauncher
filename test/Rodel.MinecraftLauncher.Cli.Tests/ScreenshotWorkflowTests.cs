// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises the shared screenshot service through real CLI processes and external MCP execute, including NativeAOT targets.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ScreenshotWorkflowTests
{
    /// <summary>
    /// Review: Core file tests cannot prove command registration, Unicode token transport, partial-result serialization or
    /// destructive confirmation. This journey crosses CLI/MCP and inspects exact file bytes and a real recycle receipt.
    /// </summary>
    [TestMethod]
    public async Task Screenshots_CliAndMcp_PreserveCopiesAndRequireRemovalConfirmation()
    {
        using var cli = new CliSandbox();
        var instance = (await cli.RunAsync("instance", "create", "Screenshot workflow", "--version", "synthetic")).ReadEnvelope(0)["data"]!;
        var id = instance["id"]!.GetValue<string>();
        var empty = (await cli.RunAsync("screenshot", "list", id)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(empty["exists"]!.GetValue<bool>());
        var directory = empty["directory"]!.GetValue<string>();
        Directory.CreateDirectory(directory);
        const string originalName = "湖边 dusk.png";
        const string newName = "日落 view.png";
        byte[] image = [17, 29, 43];
        await File.WriteAllBytesAsync(Path.Combine(directory, originalName), image, TestContext.CancellationToken);
        await File.WriteAllBytesAsync(Path.Combine(directory, "second.png"), [2, 4, 6], TestContext.CancellationToken);
        await using var mcp = await McpSession.ConnectAsync(cli);
        var renamed = await mcp.CallAsync("execute", new() { ["arguments"] = new JsonArray("screenshot", "rename", id, originalName, newName) });
        Assert.AreEqual("succeeded", renamed["status"]?.ToString(), renamed.ToJsonString());
        var source = renamed["data"]!["path"]!.GetValue<string>();
        Assert.IsFalse(File.Exists(Path.Combine(directory, originalName)));
        CollectionAssert.AreEqual(image, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));

        var output = Path.Combine(cli.Root, "export copies");
        Directory.CreateDirectory(output);
        await File.WriteAllBytesAsync(Path.Combine(output, newName), [8, 9], TestContext.CancellationToken);
        var partial = (await cli.RunAsync("screenshot", "export", id, output, "second.png", newName)).ReadEnvelope(1);
        Assert.AreEqual("SCREENSHOT_PARTIAL_FAILURE", partial["error"]?["code"]?.ToString());
        Assert.AreEqual(1, partial["data"]?["completed"]?.GetValue<int>());
        Assert.AreEqual("SCREENSHOT_DESTINATION_EXISTS", partial["data"]?["items"]?[1]?["errorCode"]?.ToString());
        CollectionAssert.AreEqual(new byte[] { 2, 4, 6 }, await File.ReadAllBytesAsync(Path.Combine(output, "second.png"), TestContext.CancellationToken));
        CollectionAssert.AreEqual(new byte[] { 8, 9 }, await File.ReadAllBytesAsync(Path.Combine(output, newName), TestContext.CancellationToken));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "second.png")), "Export must not move its source.");

        var unconfirmed = (await cli.RunAsync("screenshot", "remove", id, newName)).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", unconfirmed["error"]?["code"]?.ToString());
        CollectionAssert.AreEqual(image, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        string? recycled = null;
        try
        {
            var removed = await mcp.CallAsync("execute", new() { ["arguments"] = new JsonArray("screenshot", "remove", id, newName, "--confirm") });
            recycled = removed["data"]?["items"]?[0]?["destination"]?.GetValue<string>();
            Assert.AreEqual("succeeded", removed["status"]?.ToString(), removed.ToJsonString());
            Assert.IsNotNull(recycled);
            Assert.IsFalse(File.Exists(source));
            CollectionAssert.AreEqual(image, await File.ReadAllBytesAsync(recycled, TestContext.CancellationToken));
            var remaining = (await cli.RunAsync("screenshot", "list", id)).ReadEnvelope(0)["data"]!["files"]!.AsArray();
            Assert.AreEqual("second.png", remaining.Single()!["fileName"]!.GetValue<string>());
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

        var directSource = Path.Combine(directory, "second.png");
        var directUnconfirmed = (await cli.RunAsync("screenshot", "remove", id, "second.png", "--permanent")).ReadEnvelope(2);
        Assert.AreEqual("INVALID_ARGUMENT", directUnconfirmed["error"]?["code"]?.ToString());
        CollectionAssert.AreEqual(new byte[] { 2, 4, 6 }, await File.ReadAllBytesAsync(directSource, TestContext.CancellationToken));
        var deleted = await mcp.CallAsync("execute", new()
        {
            ["arguments"] = new JsonArray("screenshot", "remove", id, "second.png", "--permanent", "--confirm")
        });
        Assert.AreEqual("succeeded", deleted["status"]?.ToString(), deleted.ToJsonString());
        Assert.AreEqual(1, deleted["data"]?["completed"]?.GetValue<int>());
        Assert.IsNull(deleted["data"]?["items"]?[0]?["destination"]);
        Assert.IsFalse(File.Exists(directSource));
        CollectionAssert.AreEqual(image, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        CollectionAssert.AreEqual(new byte[] { 2, 4, 6 }, await File.ReadAllBytesAsync(Path.Combine(output, "second.png"), TestContext.CancellationToken));
    }

    /// <summary>Gets the runner cancellation signal for owned fixture files and process-independent assertions.</summary>
    public TestContext TestContext { get; set; } = null!;
}