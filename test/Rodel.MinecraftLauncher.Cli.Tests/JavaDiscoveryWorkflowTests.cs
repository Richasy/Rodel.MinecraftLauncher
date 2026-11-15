namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks Java discovery observations across actual CLI hosts; probe mechanics belong to the Core tests.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaDiscoveryWorkflowTests
{
    /// <summary>Independent CLI processes must reuse a validated observation and report an in-place runtime update.</summary>
    [TestMethod]
    public async Task Import_JavaUpdated_RefreshesVersionAcrossProcesses()
    {
        using var sandbox = new CliSandbox();
        var fixture = new JavaProbeFixture(sandbox.Root);
        await VerifyProbeAsync(sandbox, fixture, "25.0.1");
        await VerifyProbeAsync(sandbox, fixture, "25.0.1");
        Assert.AreEqual(1, fixture.Calls, "A second CLI process must not repeat an unchanged probe.");
        var release = Path.Combine(fixture.Home, "release");
        var timestamp = File.GetLastWriteTimeUtc(release);
        fixture.SetVersion("25.0.2");
        File.SetLastWriteTimeUtc(release, timestamp);
        await VerifyProbeAsync(sandbox, fixture, "25.0.2");
        Assert.AreEqual(2, fixture.Calls);
    }

    /// <summary>Changing inherited JVM options invalidates cached observations without modifying the test host's environment.</summary>
    [TestMethod]
    public async Task Import_JavaOptionsChanged_InvalidatesAcrossProcesses()
    {
        using var sandbox = new CliSandbox();
        var fixture = new JavaProbeFixture(sandbox.Root);
        await VerifyProbeAsync(sandbox, fixture, "25.0.1", "-Dfile.encoding=UTF-8");
        await VerifyProbeAsync(sandbox, fixture, "25.0.1", "-Dfile.encoding=UTF-8");
        Assert.AreEqual(1, fixture.Calls);
        await VerifyProbeAsync(sandbox, fixture, "25.0.1", "-Dfile.encoding=US-ASCII");
        Assert.AreEqual(2, fixture.Calls);
    }

    private static async Task VerifyProbeAsync(CliSandbox sandbox, JavaProbeFixture fixture, string expectedVersion, string? javaOptions = null)
    {
        var start = sandbox.CreateStartInfo("java", "import", fixture.Executable, "--id", "probe-" + Guid.NewGuid().ToString("N"));
        if (javaOptions is not null)
            start.Environment["JAVA_TOOL_OPTIONS"] = javaOptions;
        var result = await CliSandbox.RunAsync(start);
        Assert.AreEqual(expectedVersion, result.ReadEnvelope(0)["data"]?["fullVersion"]?.GetValue<string>());
    }
}