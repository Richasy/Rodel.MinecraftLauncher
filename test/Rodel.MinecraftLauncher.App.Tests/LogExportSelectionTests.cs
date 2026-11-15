// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Settings;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects launcher/game scope selection and publication through the real controller and Core files.</summary>
[TestClass]
public sealed class LogExportSelectionTests
{
    /// <summary>Review: launcher export must work with no game history and protect an existing destination until the actual picker callback approves that exact path. This crosses the real staging/publishing path; no picker or window is claimed.</summary>
    [TestMethod]
    public void LauncherExport_NoGameHistory_RequiresExactReplacementApproval() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var home = new HomeController(scope.Client);
        using var controller = new DiagnosticsExportController(scope.Client, home);
        await controller.LoadAsync();
        Assert.IsEmpty(controller.State.AllSessions);
        var destination = Path.Combine(scope.Root, "existing.zip");
        await File.WriteAllTextAsync(destination, "preserved-output", TestContext.CancellationToken);
        controller.SetDestination(destination);
        Assert.IsTrue(controller.State.CanExport);
        await controller.ExportAsync();
        Assert.AreEqual("EXPORT_DESTINATION_EXISTS", controller.State.ErrorCode);
        Assert.AreEqual("preserved-output", await File.ReadAllTextAsync(destination, TestContext.CancellationToken));
        controller.SetDestination(destination, replacementApproved: true);
        await controller.ExportAsync();
        Assert.IsTrue(controller.State.Completed, controller.State.Error);
        Assert.IsFalse(home.HasOwnedWork);
        using var archive = ZipFile.OpenRead(destination);
        Assert.IsNotNull(archive.GetEntry("manifest.json"));
        Assert.IsNull(archive.GetEntry("session.json"));
        Assert.DoesNotContain(entry => entry.FullName.Contains("stdout", StringComparison.Ordinal), archive.Entries);
    });

    /// <summary>Review: repeated launches belong under one instance. Changing the instance must clear the old run selection before exporting; an independent ZIP read must contain only the newly selected game's bytes. Only historical session data is synthetic.</summary>
    [TestMethod]
    public void GameExport_GroupsRunsByInstance_AndInvalidatesOldSelection() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        using var home = new HomeController(scope.Client);
        var first = await scope.Client.Instances.CreateAsync("Same instance name", "26.3", TestContext.CancellationToken);
        var second = await scope.Client.Instances.CreateAsync("Same instance name", "26.3", TestContext.CancellationToken);
        for (var index = 0; index < 3; index++) await AddRunAsync(scope, first.Id, "first-game-output", index);
        await AddRunAsync(scope, second.Id, "second-game-output", 4);
        using var controller = new DiagnosticsExportController(scope.Client, home, launcher: false);
        await controller.LoadAsync();
        Assert.HasCount(2, controller.State.Instances);
        controller.SelectInstance(Array.FindIndex(controller.State.Instances, instance => instance.Id == first.Id));
        Assert.HasCount(3, controller.State.Sessions);
        controller.Select(1);
        controller.SetDestination(Path.Combine(scope.Root, "game.zip"));
        Assert.IsTrue(controller.State.CanExport);
        controller.SelectInstance(Array.FindIndex(controller.State.Instances, instance => instance.Id == second.Id));
        Assert.HasCount(1, controller.State.Sessions);
        Assert.IsNull(controller.State.Session);
        Assert.IsFalse(controller.State.CanExport);
        controller.Select(0);
        controller.SetMode(1);
        await controller.ExportAsync();
        Assert.IsTrue(controller.State.Completed, controller.State.Error);
        using var archive = ZipFile.OpenRead(controller.State.Destination);
        using var reader = new StreamReader(archive.GetEntry("stdoutLog.txt")!.Open());
        Assert.AreEqual("second-game-output", await reader.ReadToEndAsync(TestContext.CancellationToken));
        Assert.DoesNotContain(entry => entry.FullName.StartsWith("launcher/", StringComparison.Ordinal), archive.Entries);
    });

    private async Task AddRunAsync(StateTestScope scope, string instance, string contents, int offset)
    {
        var id = Guid.NewGuid().ToString("N");
        var file = Path.Combine(scope.Root, id + ".log");
        await File.WriteAllTextAsync(file, contents, TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(scope.Client.Paths.SessionsDirectory, id + ".json"), new JsonObject
        {
            ["id"] = id,
            ["instanceId"] = instance,
            ["status"] = "exited",
            ["exitCode"] = 0,
            ["createdAt"] = DateTimeOffset.UtcNow.AddSeconds(offset).ToString("O"),
            ["stdoutLog"] = file,
        }, TestContext.CancellationToken);
    }

    /// <summary>Gets cancellation for owned fixture I/O and exports.</summary>
    public TestContext TestContext { get; set; } = null!;
}