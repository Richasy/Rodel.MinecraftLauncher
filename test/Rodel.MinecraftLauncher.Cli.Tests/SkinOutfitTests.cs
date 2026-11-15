// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Skins;
using Rodel.MinecraftLauncher.Core.Skins;
using Rodel.MinecraftLauncher.Core.Storage;
using StbImageSharp;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises separate cape/skin storage and pairing through actual command processes.</summary>
[TestClass]
public sealed class SkinOutfitTests
{
    // Author review: geometry-only tests cannot establish that metadata survives .rmcs persistence or
    // that exporting a combined preview does not bake the reference into either saved texture.
    // Create the character through CLI and cape/pair/preview through real stdio MCP: a missing
    // Cape template/model or Solo/Companion mapping
    // must change the observed image/store, not just satisfy a schema assertion. Reuse this flow
    // rather than adding a duplicate journey; the bound-profile restriction has its own existing case.
    /// <summary>Pairing survives reopening; editing/exporting a cape preserves the paired skin byte for byte.</summary>
    [TestMethod]
    public async Task PairedWorks_SaveExportAndReopenIndependently()
    {
        using var sandbox = new CliSandbox();
        var service = new SkinService(new LauncherPaths(sandbox.Root));
        var player = (await sandbox.RunAsync("skin", "create", "--model", "Classic", "--template", "classic", "--name", "Player")).ReadEnvelope(0);
        var skin = player["data"]!["documentId"]!.GetValue<string>();
        await using var mcp = await McpSession.ConnectAsync(sandbox, "skins");
        var created = await mcp.CallAsync("skin_create", new JsonObject { ["model"] = "Cape", ["template"] = "cape", ["name"] = "Cape" });
        Assert.AreEqual("succeeded", created["status"]!.GetValue<string>(), created.ToJsonString());
        var cape = created["data"]!["documentId"]!.GetValue<string>();
        var original = File.ReadAllBytes(Path.Combine(sandbox.Root, "skins", skin + ".rmcs"));
        var templates = await mcp.CallAsync("skin_templates", []);
        Assert.ContainsSingle(item => item?["templateId"]?.GetValue<string>() == "cape" && item?["suggestedModel"]?.GetValue<string>() == "Cape", templates["data"]!.AsArray());
        var pairedWork = await mcp.CallAsync("skin_outfit", new JsonObject { ["documentId"] = cape, ["expectedRevision"] = 0, ["companionId"] = skin });
        Assert.AreEqual("succeeded", pairedWork["status"]!.GetValue<string>(), pairedWork.ToJsonString());
        Assert.AreEqual(skin, new SkinService(new LauncherPaths(sandbox.Root)).Describe(cape).CompanionId);
        Assert.AreEqual(0L, service.Read(cape).Revision);
        var session = service.OpenEditor(cape);
        await session.ApplyAsync(new() { Tool = SkinTool.Pencil, Color = "#FFFF00", Surface = new() { Part = SkinPart.Cape, Layer = SkinLayer.Base, Face = SkinFace.Front }, X = 4, Y = 5 }, TestContext.CancellationToken);
        var png = Path.Combine(sandbox.Root, "cape.png");
        (await sandbox.RunAsync("skin", "export", cape, "--output", png)).ReadEnvelope(0);
        var decoded = ImageResult.FromMemory(File.ReadAllBytes(png), ColorComponents.RedGreenBlueAlpha);
        Assert.AreEqual(64, decoded.Width); Assert.AreEqual(32, decoded.Height);
        CollectionAssert.AreEqual(service.Read(cape).Pixels, SkinPng.Decode(File.ReadAllBytes(png), SkinModel.Cape).Pixels);
        var paired = Path.Combine(sandbox.Root, "paired.png"); var solo = Path.Combine(sandbox.Root, "solo.png");
        var pairImage = await mcp.PreviewAsync(new JsonObject { ["documentId"] = cape, ["output"] = paired });
        var soloImage = await mcp.PreviewAsync(new JsonObject { ["documentId"] = cape, ["output"] = solo, ["solo"] = true });
        CollectionAssert.AreEqual(File.ReadAllBytes(paired), pairImage);
        CollectionAssert.AreEqual(File.ReadAllBytes(solo), soloImage);
        Assert.IsFalse(File.ReadAllBytes(paired).SequenceEqual(File.ReadAllBytes(solo)));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(Path.Combine(sandbox.Root, "skins", skin + ".rmcs")));
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetCompanionAsync(cape, 1, cape, TestContext.CancellationToken));
        var copy = Path.Combine(sandbox.Root, "cape copy.rmcs");
        await session.SaveProjectAsAsync(copy, cancellationToken: TestContext.CancellationToken);
        var (Session, Name) = await service.OpenProjectFileAsync(copy, TestContext.CancellationToken);
        CollectionAssert.AreEqual(session.Render().Pixels.ToArray(), Session.Render().Pixels.ToArray());
        var stale = await mcp.CallAsync("skin_outfit", new JsonObject { ["documentId"] = cape, ["expectedRevision"] = 0, ["clear"] = true });
        Assert.AreEqual("failed", stale["status"]!.GetValue<string>());
        Assert.AreEqual(skin, service.Describe(cape).CompanionId);
        var cleared = await mcp.CallAsync("skin_outfit", new JsonObject { ["documentId"] = cape, ["expectedRevision"] = 1, ["clear"] = true });
        Assert.AreEqual("succeeded", cleared["status"]!.GetValue<string>(), cleared.ToJsonString());
        Assert.IsNull(service.Describe(cape).CompanionId);
    }

    /// <summary>Gets or sets the cancellation used for isolated process and file operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}