// Copyright (c) Richasy. All rights reserved.

using System.Diagnostics;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Runs the production Java dispatcher against controlled client/server executors. Actual Minecraft compatibility is separately verified in isolated games.</summary>
[TestClass]
public sealed class GameBridgeJavaTests
{
    /// <summary>Review: native-executor dispatch must preserve legacy cheats and independently configured scoped guest permissions while reusing LAN; a world-switch barrier must prevent publishing a replacement world.</summary>
    [TestMethod]
    [DataRow("lan_publish")]
    [DataRow("lan_stale")]
    [DataRow("lan_scoped")]
    [DataRow("lan_scoped_stale")]
    public async Task JavaLan_PublicationPreservesPermissionsAndWorldAuthority(string scenario) => await RunScenarioAsync(scenario);

    /// <summary>Review: missing publication APIs must reject before mutation; treating a known binding failure as unknown would block recovery despite no listener being opened.</summary>
    [TestMethod]
    public async Task JavaLan_UnsupportedBinding_RejectsBeforeNativeEffect() => await RunScenarioAsync("lan_unsupported");

    /// <summary>Review: loaded-recipe replacement and native ingredient alternatives are distinct from existing ownership/input tests; a removed recipe or invented optional-mod progress would mislead modpack guidance.</summary>
    [TestMethod]
    public async Task JavaKnowledge_LoadedRecipesAndMissingProviders_PreserveActualEvidence()
    {
        await RunScenarioAsync("knowledge_recipes");
    }

    /// <summary>Review: optional public APIs are substituted in a separate JVM classpath to protect actual player-team progress, refreshed completion, translated book text and native visibility without claiming real mod compatibility.</summary>
    [TestMethod]
    public async Task JavaKnowledge_PlayerTeamAndBookUnlock_UseCurrentProgress()
    {
        await RunScenarioAsync("knowledge_progress");
    }

    /// <summary>Review: the Java AI ingress must sample the caller's original world, multiplex its exact response and fail pending futures on disconnect without replay. The fixture holds the actual client executor across the world switch.</summary>
    [TestMethod]
    public async Task JavaAiIngress_WorldSwitchAndDisconnect_DoNotRetargetOrReplay()
    {
        await RunScenarioAsync("ai_ingress");
    }

    /// <summary>Review: explicit executor barriers hold an edit before mutation, exposing cancellation and world-switch races. The unknown row fails only after mutation, so a second write would independently fail its write-count oracle.</summary>
    [TestMethod]
    [DataRow("cancel")]
    [DataRow("stale")]
    [DataRow("remote")]
    [DataRow("connection_switch")]
    [DataRow("unknown")]
    public async Task JavaDispatcher_ControlledAuthorityBoundary_PreservesEditOutcome(string scenario)
    {
        await RunScenarioAsync(scenario);
    }

    /// <summary>Review: real JVM class initialization and exact virtual-method mappings protect loader bootstrap. Sentinels fail if observing a definition initializes game code or touches Fabric before its entrypoint.</summary>
    [TestMethod]
    [DataRow("bootstrap")]
    [DataRow("inherited_mapping")]
    [DataRow("named_inherited")]
    [DataRow("class_initialization")]
    public async Task JavaBindings_BootstrapAndVirtualNames_DoNotGuessOrInitializeGame(string scenario)
    {
        await RunScenarioAsync(scenario);
    }

    /// <summary>Review: the real client hasChunk method always succeeds, so that sentinel must not turn missing chunks into air. The production query must use the native no-create cache and observe later synchronization without retaining a stale loaded flag.</summary>
    [TestMethod]
    public async Task JavaRegion_ActualCacheBoundary_DistinguishesUnloadedFromAir()
    {
        await RunScenarioAsync("unloaded_region");
    }

    /// <summary>Review: the actual rewritten getter is JVM-verified, while a frozen production input lease exposes authority changes without relying on an observer tick. Physical input must remain untouched.</summary>
    [TestMethod]
    [DataRow("input_getter")]
    [DataRow("input_modified_getter")]
    [DataRow("input_authority")]
    [DataRow("input_timing")]
    [DataRow("input_ui")]
    [DataRow("input_physical_use")]
    [DataRow("input_remote_release")]
    public async Task JavaInput_NativeReadBoundary_RevokesOnlyBridgeInput(string scenario)
    {
        await RunScenarioAsync(scenario);
    }

    /// <summary>Review: an alive player has a known unmet respawn precondition. It must be rejected before applying, rather than retained as an unknown effect that blocks subsequent AI actions.</summary>
    [TestMethod]
    public async Task JavaRespawn_AlivePlayer_IsRejectedBeforeNativeRequest()
    {
        await RunScenarioAsync("respawn_alive");
    }

    private async Task RunScenarioAsync(string scenario)
    {
        var root = Path.Combine(Path.GetTempPath(), "Rodel.Core.Tests", "java bridge " + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var classes = Path.Combine(AppContext.BaseDirectory, "java-bridge-tests");
            var start = new ProcessStartInfo((await File.ReadAllTextAsync(Path.Combine(classes, "java-path.txt"), TestContext.CancellationToken)).Trim())
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            var classpath = scenario == "knowledge_progress" ? classes + Path.PathSeparator + Path.Combine(classes, "optional-mods") : classes;
            foreach (var argument in new[] { "-cp", classpath, "io.github.richasy.rodel.bridge.GameBridgeChecks", scenario, root }) start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEndAsync(TestContext.CancellationToken);
            var error = process.StandardError.ReadToEndAsync(TestContext.CancellationToken);
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(20));
            try { await process.WaitForExitAsync(deadline.Token); }
            catch (OperationCanceledException) { if (!process.HasExited) process.Kill(true); throw; }

            Assert.AreEqual(0, process.ExitCode, await error);
            Assert.Contains("PASS " + scenario, await output);
        }
        finally { Directory.Delete(root, true); }
    }

    /// <summary>Gets the runner's cancellation signal for the owned JVM process.</summary>
    public TestContext TestContext { get; set; } = null!;
}