// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Packs;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects creation-form source changes using real Core archive inspection and isolated storage.</summary>
[TestClass]
public sealed class PackImportSelectionTests
{
    /// <summary>Review: choosing another pack must refresh an automatically suggested name, including a MRPACK stored as ZIP, while preserving an explicit player name. No task should start during inspection. Real archive bytes and Core parsing replace a mocked inspection result.</summary>
    [TestMethod]
    public void ArchiveSelection_RefreshesSuggestedName_AndPreservesPlayerName() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var home = new HomeController(scope.Client);
        using var controller = new PackImportController(scope.Client, home);
        var first = CreatePack(scope.Root, "first.mrpack", "First pack");
        var second = CreatePack(scope.Root, "second.zip", "Other pack");

        controller.SetSource(first);
        await controller.InspectAsync();
        Assert.AreEqual("First pack", controller.State.Name);
        Assert.IsTrue(controller.State.CanImport, controller.State.Error);

        controller.SetSource(second);
        Assert.IsFalse(controller.State.CanImport);
        await controller.InspectAsync();
        Assert.AreEqual("Other pack", controller.State.Name);
        Assert.AreEqual("mrpack", controller.State.Inspection?.Format);

        controller.SetName("My farm");
        controller.SetSource(first);
        await controller.InspectAsync();
        Assert.AreEqual("My farm", controller.State.Name);
        Assert.AreEqual("First pack", controller.State.Inspection?.Name?.ToString());
        Assert.IsTrue(controller.State.CanImport, controller.State.Error);
        Assert.IsEmpty(scope.Client.Jobs.List());
        Assert.IsFalse(home.HasOwnedWork);
    });

    private static string CreatePack(string root, string fileName, string name)
    {
        var path = Path.Combine(root, fileName);
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open());
        writer.Write(new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = name,
            ["versionId"] = "test-1",
            ["dependencies"] = new JsonObject { ["minecraft"] = "1.20.1" },
            ["files"] = new JsonArray()
        }.ToJsonString());
        return path;
    }
}