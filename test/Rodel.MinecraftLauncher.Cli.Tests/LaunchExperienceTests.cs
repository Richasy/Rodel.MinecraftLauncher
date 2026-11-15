using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects launch-time game options and process-versus-window evidence through real CLI sessions.</summary>
[TestClass]
[TestCategory("Integration")]
[DoNotParallelize]
public sealed class LaunchExperienceTests
{
    /// <summary>
    /// The reporting worker supplies a real initially hidden window; only an explicit file barrier shows it.
    /// This distinguishes actual visible-window evidence from process creation, elapsed time and unrelated windows.
    /// Plan-only must not edit options; the real monitor must apply language and window mode before starting the child.
    /// Locale variants exercise App's three targets, legacy asset spelling and omission preserving player choice.
    /// The saved fullscreen=true fixture distinguishes a windowed launch from merely removing --fullscreen;
    /// the explicit true variant protects the opposite choice without duplicating the process lifecycle fixture.
    /// Persisted global true and its one-shot false override protect consumption/precedence, beyond configuration storage tests.
    /// </summary>
    [TestMethod]
    [DataRow("zh_cn", "zh_cn", false, null, null)]
    [DataRow("en_us", "en_us", false, false, true)]
    [DataRow("ar_sa", "ar_sa", false, null, null)]
    [DataRow("zh_cn", "zh_CN", true, null, null)]
    [DataRow(null, "de_de", false, null, null)]
    [DataRow(null, "de_de", false, true, null)]
    [DataRow(null, "de_de", false, null, true)]
    public async Task Launch_LanguageAndHiddenWindow_ApplyOnlyAtStartAndObserveVisibility(string? language, string expected, bool legacy, bool? fullscreen, bool? storedFullscreen)
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = await PrepareAsync(cli, legacy);
        if (storedFullscreen is { } stored)
            (await cli.RunAsync("config", "set", "fullscreen", stored.ToString().ToLowerInvariant())).ReadEnvelope(0);
        var expectedFullscreen = fullscreen ?? storedFullscreen ?? false;
        var game = instance["gameDirectory"]!.GetValue<string>();
        var optionsFile = Path.Combine(game, "options.txt");
        const string original = "futureOption:unchanged\r\nlang:de_de\r\nfullscreen:true\r\nresourcePacks:[\"vanilla\"]\r\n# retained tail";
        var encoding = new UTF8Encoding(true);
        await File.WriteAllTextAsync(optionsFile, original, encoding, TestContext.CancellationToken);
        var before = await File.ReadAllBytesAsync(optionsFile, TestContext.CancellationToken);
        string[] arguments = ["launch", instance["id"]!.GetValue<string>(), "--offline", "--java", java.Executable,
            .. language is null ? [] : new[] { "--game-language", language },
            .. fullscreen is null ? [] : new[] { "--fullscreen=" + fullscreen.Value.ToString().ToLowerInvariant() }];
        var plan = (await cli.RunAsync([.. arguments, "--plan"])).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(language is null ? null : expected, plan["gameLanguage"]?.ToString());
        Assert.AreEqual(expectedFullscreen, plan["fullscreen"]?.GetValue<bool>());
        CollectionAssert.AreEqual(before, await File.ReadAllBytesAsync(optionsFile, TestContext.CancellationToken));
        var started = (await cli.RunAsync(arguments)).ReadEnvelope(0)["data"]!;
        var session = started["id"]!.GetValue<string>();
        using var child = Process.GetProcessById(started["processId"]!.GetValue<int>());
        using var monitor = Process.GetProcessById(started["monitorId"]!.GetValue<int>());
        try
        {
            await WaitUntilAsync(() => File.Exists(Path.Combine(java.Home, "window-created")));
            var hidden = (await cli.RunAsync("process", "show", session)).ReadEnvelope(0)["data"]!;
            Assert.AreEqual("running", hidden["status"]?.ToString());
            Assert.AreEqual("process_started", hidden["readiness"]?.ToString(), "A hidden window must not trigger launcher minimization.");
            var expectedText = original.Replace("lang:de_de", "lang:" + expected, StringComparison.Ordinal)
                .Replace("fullscreen:true", "fullscreen:" + (expectedFullscreen ? "true" : "false"), StringComparison.Ordinal);
            var expectedBytes = encoding.GetPreamble().Concat(encoding.GetBytes(expectedText)).ToArray();
            CollectionAssert.AreEqual(expectedBytes, await File.ReadAllBytesAsync(optionsFile, TestContext.CancellationToken));

            await File.WriteAllTextAsync(Path.Combine(java.Home, "show-window"), string.Empty, TestContext.CancellationToken);
            await WaitUntilAsync(() => { child.Refresh(); return child.MainWindowHandle != 0; });
            var visible = (await cli.RunAsync("process", "show", session)).ReadEnvelope(0)["data"]!;
            Assert.AreEqual("window_visible", visible["readiness"]?.ToString());
            Assert.IsNull(visible["windowObservationWarning"]);
            Assert.IsTrue((await cli.RunAsync("process", "stop", session)).ReadEnvelope(0)["data"]!["stopRequested"]!.GetValue<bool>());
            await child.WaitForExitAsync(TestContext.CancellationToken);
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(TestContext.CancellationToken);
            await WaitForMonitorAsync(monitor);
        }
    }

    /// <summary>A child that exits without a visible window must report failure, never window-visible readiness.</summary>
    [TestMethod]
    public async Task Launch_ExitBeforeWindow_ReportsFailureWithoutVisibleEvidence()
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = await PrepareAsync(cli, false);
        var started = (await cli.RunAsync("launch", instance["id"]!.GetValue<string>(), "--offline", "--java", java.Executable)).ReadEnvelope(0)["data"]!;
        var session = started["id"]!.GetValue<string>();
        using var child = Process.GetProcessById(started["processId"]!.GetValue<int>());
        using var monitor = Process.GetProcessById(started["monitorId"]!.GetValue<int>());
        try
        {
            await File.WriteAllTextAsync(Path.Combine(java.Home, "exit-before-window"), string.Empty, TestContext.CancellationToken);
            await child.WaitForExitAsync(TestContext.CancellationToken);
            await WaitForMonitorAsync(monitor);
            var exited = (await cli.RunAsync("process", "show", session)).ReadEnvelope(0)["data"]!;
            Assert.AreEqual("exited", exited["status"]?.ToString());
            Assert.AreEqual(9, exited["exitCode"]?.GetValue<int>());
            Assert.AreNotEqual("window_visible", exited["readiness"]?.ToString());
        }
        finally
        {
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            await child.WaitForExitAsync(TestContext.CancellationToken);
            await WaitForMonitorAsync(monitor);
        }
    }

    /// <summary>
    /// Language synchronization is not an execution prerequisite: a read-only options file must stay
    /// byte-identical while a real child still starts, with the failed synchronization explicitly reported.
    /// This exercises the actual Windows replacement failure rather than a substituted settings service.
    /// </summary>
    [TestMethod]
    public async Task Launch_ReadOnlyOptions_RetainsBytesAndStartsWithExplicitWarning()
    {
        using var cli = new CliSandbox();
        var java = new JavaProbeFixture(cli.Root);
        var instance = await PrepareAsync(cli, false);
        var file = Path.Combine(instance["gameDirectory"]!.GetValue<string>(), "options.txt");
        const string original = "lang:de_de\nfuture:retained\n";
        await File.WriteAllTextAsync(file, original, TestContext.CancellationToken);
        File.SetAttributes(file, FileAttributes.ReadOnly);
        Process? child = null;
        Process? monitor = null;
        try
        {
            var started = (await cli.RunAsync("launch", instance["id"]!.GetValue<string>(), "--offline",
                "--java", java.Executable, "--game-language", "zh_cn")).ReadEnvelope(0)["data"]!;
            child = Process.GetProcessById(started["processId"]!.GetValue<int>());
            monitor = Process.GetProcessById(started["monitorId"]!.GetValue<int>());
            Assert.AreEqual("running", started["status"]?.ToString());
            Assert.IsFalse(string.IsNullOrWhiteSpace(started["gameLanguageWarning"]?.ToString()));
            Assert.IsFalse(string.IsNullOrWhiteSpace(started["windowModeWarning"]?.ToString()));
            Assert.Contains(item => item!.ToString().Contains("Game language", StringComparison.Ordinal), started["warnings"]!.AsArray());
            Assert.AreEqual(original, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
        }
        finally
        {
            File.SetAttributes(file, FileAttributes.Normal);
            if (child is not null)
            {
                if (!child.HasExited) child.Kill(entireProcessTree: true);
                await child.WaitForExitAsync(TestContext.CancellationToken);
                child.Dispose();
            }

            if (monitor is not null)
            {
                try { await WaitForMonitorAsync(monitor); }
                finally { monitor.Dispose(); }
            }
        }
    }

    private static async Task<JsonNode> PrepareAsync(CliSandbox cli, bool legacy)
    {
        var instance = (await cli.RunAsync("instance", "create", "window-fixture", "--version", "synthetic")).ReadEnvelope(0)["data"]!;
        var minecraft = instance["minecraftDirectory"]!.GetValue<string>();
        var directory = Path.Combine(minecraft, "versions", "synthetic");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "synthetic.jar"), "synthetic client, not runnable Java");
        var metadata = new JsonObject
        {
            ["id"] = "synthetic",
            ["mainClass"] = "Synthetic.Window",
            ["javaVersion"] = new JsonObject { ["majorVersion"] = 25 },
            ["libraries"] = new JsonArray(),
            ["arguments"] = new JsonObject { ["jvm"] = new JsonArray(), ["game"] = new JsonArray() }
        };
        if (legacy)
        {
            var references = JsonNode.Parse(File.ReadAllText(Path.Combine(minecraft, "dependencies.json")))!;
            var assets = Path.Combine(cli.Root, "dependencies", "assets", references["assets"]!.GetValue<string>());
            var payload = Encoding.UTF8.GetBytes("fixture.language=synthetic");
            var hash = Convert.ToHexStringLower(SHA1.HashData(payload));
            var objects = Path.Combine(assets, "objects", hash[..2]);
            Directory.CreateDirectory(objects);
            File.WriteAllBytes(Path.Combine(objects, hash), payload);
            var indexes = Path.Combine(assets, "indexes");
            Directory.CreateDirectory(indexes);
            File.WriteAllText(Path.Combine(indexes, "legacy.json"), new JsonObject
            {
                ["objects"] = new JsonObject
                {
                    ["minecraft/lang/zh_CN.lang"] = new JsonObject { ["hash"] = hash, ["size"] = payload.Length }
                }
            }.ToJsonString());
            metadata["assetIndex"] = new JsonObject { ["id"] = "legacy" };
        }

        File.WriteAllText(Path.Combine(directory, "synthetic.json"), metadata.ToJsonString());
        return instance;
    }

    private static async Task WaitForMonitorAsync(Process monitor)
    {
        // A terminal record can precede monitor teardown and release of its marker files.
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await monitor.WaitForExitAsync(deadline.Token);
    }

    private async Task WaitUntilAsync(Func<bool> predicate)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (!predicate()) await Task.Delay(20, deadline.Token);
    }

    /// <summary>Gets or sets the runner deadline used by the owned processes and visibility barriers.</summary>
    public TestContext TestContext { get; set; } = null!;
}