// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Runtime;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects the pack runtime commit boundary independently of task history and downloaded-byte caches.</summary>
[TestClass]
public sealed class PackReadinessTests
{
    /// <summary>
    /// Author review: preparation-cancellation coverage cannot detect the next launch silently selecting
    /// vanilla. A complete local base plus an uncommitted Forge declaration must reject a vanilla plan,
    /// and real launch must reach preparation before any session. After a deliberate local runtime
    /// commit, a new client must recognize readiness, then notice a removed required JAR.
    /// </summary>
    [TestMethod]
    public async Task CanceledRuntime_LaunchPreparesRatherThanFallingBackAndReopensCurrentReadiness()
    {
        using var fixture = new JavaProbeFixture();
        var paths = fixture.Paths;
        paths.EnsureDirectories();
        var archive = Path.Combine(paths.Root, "forge.mrpack");
        using (var zip = ZipFile.Open(archive, ZipArchiveMode.Create))
        using (var writer = new StreamWriter(zip.CreateEntry("modrinth.index.json").Open()))
            writer.Write("""{"formatVersion":1,"game":"minecraft","name":"Forge fixture","versionId":"one","dependencies":{"minecraft":"1.20.1","forge":"47.3.33"},"files":[]}""");
        var client = new LauncherClient(paths);
        var imported = await client.Packs.ImportAsync(archive, download: false, cancellationToken: TestContext.CancellationToken);
        var id = imported.Instance!.Id;
        var instance = client.Instances.Get(id);
        await PreparationProgressTests.WriteBaseAsync(instance.MinecraftDirectory, TestContext.CancellationToken);
        await client.Settings.SetAsync("javaPath", JsonValue.Create(fixture.Executable), instance, TestContext.CancellationToken);
        await client.Settings.SetAsync("versionId", JsonValue.Create("1.20.1"), instance, TestContext.CancellationToken);

        var refused = await Assert.ThrowsAsync<LauncherOperationException>(() => client.LaunchAsync(id, new() { PlanOnly = true }, TestContext.CancellationToken));
        Assert.AreEqual("PACK_PREPARATION_REQUIRED", refused.Code);
        Assert.AreEqual("pending", refused.Details?["preparation"]?["steps"]?[1]?["status"]?.ToString());
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        var reachedPreparation = false;
        var launchClient = new LauncherClient(paths, progress: new Callback(value =>
        {
            if (value.PackPreparation?.ActiveStep != "runtime") return;
            reachedPreparation = true;
            cancellation.Cancel();
        }));
        await Assert.ThrowsAsync<OperationCanceledException>(() => launchClient.LaunchAsync(id, cancellationToken: cancellation.Token));
        Assert.IsTrue(reachedPreparation);
        Assert.IsFalse(Directory.EnumerateFileSystemEntries(paths.SessionsDirectory).Any());
        Assert.AreEqual("1.20.1", client.Settings.GetOverrides(instance)["versionId"]?.ToString());
        Assert.IsFalse(client.Settings.GetOverrides(instance).ContainsKey("loader"));

        const string selected = "prepared-forge";
        await JsonFiles.WriteAsync(Path.Combine(instance.MinecraftDirectory, "versions", selected, selected + ".json"), new JsonObject
        {
            ["id"] = selected,
            ["inheritsFrom"] = "1.20.1",
            ["jar"] = "1.20.1",
            ["mainClass"] = "cpw.mods.bootstraplauncher.BootstrapLauncher",
            ["libraries"] = new JsonArray(new JsonObject { ["name"] = "net.minecraftforge:fmlloader:1.20.1-47.3.33" })
        }, TestContext.CancellationToken);
        await client.Runtimes.ChangeAsync(id, new RuntimeChangeOptions { VersionId = selected, Offline = true }, TestContext.CancellationToken);
        var reopened = new LauncherClient(paths);
        // Runtime selection can create a different shared-dependency generation. Populate the
        // two declared fixture files at the real planner's resolved locations, not the old slots.
        var planned = (LaunchPlan)await reopened.LaunchAsync(id, new() { PlanOnly = true }, TestContext.CancellationToken);
        foreach (var required in planned.RequiredFiles.Where(file => !file.Exists))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(required.Path)!);
            if (required.Kind == "library")
                await File.WriteAllBytesAsync(required.Path, [4, 5, 6], TestContext.CancellationToken);
            else if (required.Kind == "assetIndex")
                await JsonFiles.WriteAsync(required.Path, new JsonObject { ["objects"] = new JsonObject() }, TestContext.CancellationToken);
            else
                Assert.Fail("Unexpected missing fixture dependency: " + required.Kind);
        }

        var ready = await reopened.InspectPackPreparationAsync(id, TestContext.CancellationToken);
        var plan = (LaunchPlan)await reopened.LaunchAsync(id, new() { PlanOnly = true }, TestContext.CancellationToken);
        Assert.AreEqual("completed", ready.Preparation!.Status,
            string.Join("; ", ready.Preparation.Steps.Select(step => $"{step.Id}: {step.Status} {step.Error}"))
            + "; missing: " + string.Join(", ", plan.RequiredFiles.Where(file => !file.Exists).Select(file => file.Path))
            + "; Java: " + ready.Java?.SelectedPath);
        Assert.AreEqual(selected, ready.Runtime!.VersionId);
        Assert.AreEqual(selected, plan.VersionId);
        File.Delete(plan.RequiredFiles.Single(file => file.Kind == "library").Path);
        Assert.IsFalse((await reopened.InspectPackPreparationAsync(id, TestContext.CancellationToken)).RuntimePrepared);
    }

    /// <summary>
    /// Author review: defaults must actually select automatic heap sizing through the launch path,
    /// while an explicitly saved manual limit still wins. Existing sizing arithmetic tests do not
    /// exercise merged defaults or a saved setting. The Java subprocess is the reporting fixture.
    /// </summary>
    [TestMethod]
    public async Task Memory_DefaultAutoAndSavedManual_AreAppliedToRealLaunchPlans()
    {
        using var fixture = new JavaProbeFixture();
        var client = new LauncherClient(fixture.Paths);
        var instance = await client.Instances.CreateAsync("Memory fixture", "1.20.1", TestContext.CancellationToken);
        await PreparationProgressTests.WriteBaseAsync(instance.MinecraftDirectory, TestContext.CancellationToken);
        await client.Settings.SetAsync("javaPath", JsonValue.Create(fixture.Executable), instance, TestContext.CancellationToken);
        var automatic = (LaunchPlan)await client.LaunchAsync(instance.Id, new() { PlanOnly = true }, TestContext.CancellationToken);
        Assert.AreEqual("auto", automatic.MemorySelection!.SelectionSource);
        Assert.AreEqual("auto", client.Settings.GetEffective(instance).Values["memoryMode"]?.ToString());
        await client.Settings.SetAsync("maxMemoryMb", JsonValue.Create(4096), instance, TestContext.CancellationToken);
        var manual = (LaunchPlan)await new LauncherClient(fixture.Paths).LaunchAsync(instance.Id, new() { PlanOnly = true }, TestContext.CancellationToken);
        Assert.AreEqual("configured", manual.MemorySelection!.SelectionSource);
        Assert.Contains("-Xmx4096M", manual.Arguments);
        await client.Settings.SetAsync("memoryMode", JsonValue.Create("auto"), instance, TestContext.CancellationToken);
        var restored = (LaunchPlan)await client.LaunchAsync(instance.Id, new() { PlanOnly = true }, TestContext.CancellationToken);
        Assert.AreEqual("auto", restored.MemorySelection!.SelectionSource);
    }

    /// <summary>Gets or sets the runner deadline for the storage and reporting-Java boundaries.</summary>
    public TestContext TestContext { get; set; } = null!;

    private sealed class Callback(Action<LauncherProgress> action) : IProgress<LauncherProgress>
    {
        /// <inheritdoc/>
        public void Report(LauncherProgress value) => action(value);
    }
}