// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.App.Features.Home;
using Rodel.MinecraftLauncher.App.Features.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.App.Tests;

/// <summary>Protects memory inheritance, the home summary and shared configuration autosave recovery.</summary>
[TestClass]
public sealed class LaunchMemoryPreferenceTests
{
    /// <summary>
    /// Author review: Core sizing cases do not cross the form baseline/save mapping. An unrelated
    /// save must retain inherited automatic memory, a deliberate manual edit must persist, and
    /// returning to automatic must not validate or save a hidden invalid manual number.
    /// Extend that same journey through the actual home reader: a fixed automatic bound makes
    /// the stale 4096 MB summary distinguishable without copying the sizing algorithm or mocking Core.
    /// </summary>
    [TestMethod]
    public void AutomaticInheritance_OnlyExplicitModeAndManualEditsCreateOverrides() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Memory form fixture", "synthetic", TestContext.CancellationToken);
        Assert.IsNotNull(MemorySelection.AvailableMb(), "This Windows test needs the physical-memory query used by the home summary.");
        await scope.Client.Settings.UpdateAsync(new JsonObject { ["autoMemoryMinMb"] = 128, ["autoMemoryMaxMb"] = 128 }, cancellationToken: TestContext.CancellationToken);
        var summary = new InstanceSummary(instance.Id, instance.Name, "synthetic", false);
        var draft = await HomeLaunchSettingsService.ReadAsync(scope.Client, summary);
        Assert.IsTrue(draft.AutomaticMemory);
        draft = draft with { GameBridge = GameBridgeSettingMode.Enabled };
        Assert.IsTrue(HomeLaunchSettingsService.TryValidate(draft, out var unrelatedMemory));
        await HomeLaunchSettingsService.SaveAsync(scope.Client, draft, unrelatedMemory);
        var overrides = scope.Client.Settings.GetOverrides(instance);
        Assert.IsFalse(overrides.ContainsKey("memoryMode"));
        Assert.IsFalse(overrides.ContainsKey("maxMemoryMb"));

        draft = (await HomeLaunchSettingsService.ReadAsync(scope.Client, summary)) with { AutomaticMemory = false, Memory = "4096" };
        Assert.IsTrue(HomeLaunchSettingsService.TryValidate(draft, out var manualMemory));
        await HomeLaunchSettingsService.SaveAsync(scope.Client, draft, manualMemory);
        draft = await HomeLaunchSettingsService.ReadAsync(scope.Client, summary);
        Assert.IsFalse(draft.AutomaticMemory);
        Assert.AreEqual("4096", draft.Memory);
        Assert.AreEqual(4096, (await HomeInstanceReader.ReadAsync(scope.Client, instance.Id, TestContext.CancellationToken)).MemoryMb);

        draft = draft with { AutomaticMemory = true, Memory = "unfinished hidden input" };
        Assert.IsTrue(HomeLaunchSettingsService.TryValidate(draft, out var automaticMemory));
        await HomeLaunchSettingsService.SaveAsync(scope.Client, draft, automaticMemory);
        draft = await HomeLaunchSettingsService.ReadAsync(scope.Client, summary);
        Assert.IsTrue(draft.AutomaticMemory);
        Assert.AreEqual("4096", draft.Memory, "Switching mode retains the last valid manual value.");
        Assert.AreEqual(128, (await HomeInstanceReader.ReadAsync(scope.Client, instance.Id, TestContext.CancellationToken)).MemoryMb,
            "The home summary must show automatic sizing instead of the retained manual limit.");

        draft = draft with { AutomaticMemory = false };
        Assert.IsTrue(HomeLaunchSettingsService.TryValidate(draft, out var restoredMemory));
        await HomeLaunchSettingsService.SaveAsync(scope.Client, draft, restoredMemory);
        Assert.AreEqual(4096, (await HomeInstanceReader.ReadAsync(scope.Client, instance.Id, TestContext.CancellationToken)).MemoryMb,
            "Returning to manual must immediately restore the player's saved heap limit.");
    });

    /// <summary>
    /// Review: the existing form test calls one save at a time, so it cannot detect stale acknowledgements
    /// or wrong-instance writes. Inject the next edit from the real controller's saving notification,
    /// after its first snapshot is frozen but before that save completes, then inspect actual Core files.
    /// </summary>
    [TestMethod]
    public void AutoSave_EditsDuringSave_RetainLatestValuesAndInstanceOwnership() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var target = await scope.Client.Instances.CreateAsync("Autosave target", "synthetic", TestContext.CancellationToken);
        var other = await scope.Client.Instances.CreateAsync("Other selection", "synthetic", TestContext.CancellationToken);
        await scope.Client.Settings.UpdateAsync(new JsonObject
        {
            ["javaRuntimeId"] = "unavailable-managed-runtime",
            ["memoryMode"] = "manual",
            ["maxMemoryMb"] = 2048,
            ["future"] = new JsonObject { ["retained"] = true }
        }, instance: target, cancellationToken: TestContext.CancellationToken);
        using var home = new HomeController(scope.Client, target.Id);
        using var controller = new InstanceManagementController(scope.Client, home);
        await controller.OpenAsync(target.Id, InstanceAction.Configure);
        Assert.IsTrue(controller.State.Draft!.GlobalGameBridge, "A fresh global configuration enables the bridge.");
        var changedWhileSaving = false;
        controller.Changed += state =>
        {
            if (!state.Active || changedWhileSaving)
                return;
            changedWhileSaving = true;
            home.Select(other.Id);
            controller.ChangeDraft(draft => draft with { Memory = "4096", GameBridge = GameBridgeSettingMode.Inherit });
        };
        controller.ChangeDraft(draft => draft with { GameBridge = GameBridgeSettingMode.Disabled });
        Assert.IsTrue(await controller.FlushConfigurationAsync());
        Assert.IsTrue(changedWhileSaving, "The later edit must cross the captured-save boundary.");
        var stored = scope.Client.Settings.GetOverrides(target);
        Assert.AreEqual(4096, stored["maxMemoryMb"]!.GetValue<int>());
        Assert.IsFalse(stored.ContainsKey("gameBridgeEnabled"), "Restoring inheritance removes the override instead of pinning a value.");
        Assert.IsTrue(scope.Client.Settings.GetEffective(target).Values["gameBridgeEnabled"]!.GetValue<bool>());
        Assert.AreEqual("unavailable-managed-runtime", stored["javaRuntimeId"]!.GetValue<string>());
        Assert.IsFalse(stored.ContainsKey("javaPath"));
        Assert.IsTrue(stored["future"]!["retained"]!.GetValue<bool>());
        Assert.AreEqual(0, scope.Client.Settings.GetOverrides(other).Count, "Changing the home selection must not retarget an outstanding write.");
        Assert.AreEqual("4096", controller.State.Draft!.Memory);
        Assert.IsFalse(controller.State.Draft.HasChanges);
    });

    /// <summary>
    /// Review: immediate persistence must not turn partial player names into real accounts, discard invalid
    /// memory text, or block an unrelated bridge change. Observe the actual first write before flushing,
    /// then finish the two text fields and verify that only the final account and valid heap are stored.
    /// </summary>
    [TestMethod]
    public void AutoSave_InvalidAndUncommittedText_PreservesInputWhileSavingIndependentChanges() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Autosave text", "synthetic", TestContext.CancellationToken);
        await scope.Client.Settings.SetAsync("memoryMode", JsonValue.Create("manual"), instance, TestContext.CancellationToken);
        using var home = new HomeController(scope.Client);
        using var controller = new InstanceManagementController(scope.Client, home);
        await controller.OpenAsync(instance.Id, InstanceAction.Configure);
        var bridgeSaved = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        controller.Changed += state =>
        {
            if (!state.Active && state.Draft?.Baseline?.GameBridge == GameBridgeSettingMode.Disabled)
                bridgeSaved.TrySetResult();
        };
        controller.ChangeDraft(draft => draft with
        {
            Memory = "unfinished",
            MemoryEditing = true,
            Player = "Part",
            PlayerEditing = true,
            GameBridge = GameBridgeSettingMode.Disabled
        });
        await bridgeSaved.Task.WaitAsync(TestContext.CancellationToken);
        Assert.IsFalse(scope.Client.Settings.GetEffective(instance).Values["gameBridgeEnabled"]!.GetValue<bool>());
        Assert.AreEqual(2048, scope.Client.Settings.GetEffective(instance).Values["maxMemoryMb"]!.GetValue<int>());
        Assert.IsEmpty(scope.Client.Identities.List(), "Typing must not create intermediate offline accounts.");
        Assert.AreEqual("unfinished", controller.State.Draft!.Memory);
        Assert.AreEqual("Part", controller.State.Draft.Player);
        Assert.IsFalse(await controller.FlushConfigurationAsync(), "Invalid input must keep the panel and its validation visible.");
        Assert.AreEqual("unfinished", controller.State.Draft!.Memory);
        controller.ChangeDraft(draft => draft with { Memory = "3072" });
        Assert.IsTrue(await controller.FlushConfigurationAsync());
        Assert.AreEqual(3072, scope.Client.Settings.GetEffective(instance).Values["maxMemoryMb"]!.GetValue<int>());
        var accounts = scope.Client.Identities.List();
        Assert.HasCount(1, accounts);
        Assert.AreEqual("Part", accounts[0].Identity!.Name);
    });

    /// <summary>
    /// Review: a real file replacement refusal must not advance the baseline or allow dismissal.
    /// Restore the fixture file's attributes and flush the same draft to prove recovery without reopening.
    /// </summary>
    [TestMethod]
    public void AutoSave_FileReplacementFails_KeepsDraftAndRetriesAfterRecovery() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Autosave recovery", "synthetic", TestContext.CancellationToken);
        using var home = new HomeController(scope.Client);
        using var controller = new InstanceManagementController(scope.Client, home);
        await controller.OpenAsync(instance.Id, InstanceAction.Configure);
        var attributes = File.GetAttributes(instance.MetadataPath);
        File.SetAttributes(instance.MetadataPath, attributes | FileAttributes.ReadOnly);
        try
        {
            controller.ChangeDraft(draft => draft with { GameBridge = GameBridgeSettingMode.Disabled });
            Assert.IsFalse(await controller.FlushConfigurationAsync());
            Assert.IsGreaterThan(0, controller.State.Draft!.Error.Length);
            Assert.IsTrue(controller.State.Draft.HasChanges);
            Assert.IsTrue(scope.Client.Settings.GetEffective(instance).Values["gameBridgeEnabled"]!.GetValue<bool>());
        }
        finally
        {
            File.SetAttributes(instance.MetadataPath, attributes);
        }

        Assert.IsTrue(await controller.FlushConfigurationAsync());
        Assert.IsFalse(scope.Client.Settings.GetEffective(instance).Values["gameBridgeEnabled"]!.GetValue<bool>());
        Assert.IsFalse(controller.State.Draft!.HasChanges);
    });

    /// <summary>
    /// Review: unlike a failed write with unsaved edits, an initial read failure has no draft to lose.
    /// Corrupt only the isolated global settings and retain the actual error; dismissal must remain
    /// possible without silently rewriting the unreadable file or trapping the player in the popup.
    /// </summary>
    [TestMethod]
    public void AutoSave_InitialReadFails_AllowsDismissalWithoutRewritingConfiguration() => StateTestScope.Run(async () =>
    {
        using var scope = new StateTestScope();
        var instance = await scope.Client.Instances.CreateAsync("Unreadable configuration", "synthetic", TestContext.CancellationToken);
        var settings = Path.Combine(scope.Root, "settings.json");
        await File.WriteAllTextAsync(settings, "{broken", TestContext.CancellationToken);
        using var home = new HomeController(scope.Client);
        using var controller = new InstanceManagementController(scope.Client, home);
        var error = await Assert.ThrowsAsync<System.Text.Json.JsonException>(() => controller.OpenAsync(instance.Id, InstanceAction.Configure));
        controller.ReportError(error);
        Assert.IsNotNull(controller.State.Instance);
        Assert.IsNull(controller.State.Draft);
        Assert.IsGreaterThan(0, controller.State.Error.Length);
        Assert.IsTrue(await controller.FlushConfigurationAsync(), "A read error has no pending configuration edits to protect.");
        Assert.AreEqual("{broken", await File.ReadAllTextAsync(settings, TestContext.CancellationToken));
    });

    /// <summary>Gets or sets the runner deadline for the form's real configuration writes.</summary>
    public TestContext TestContext { get; set; } = null!;
}