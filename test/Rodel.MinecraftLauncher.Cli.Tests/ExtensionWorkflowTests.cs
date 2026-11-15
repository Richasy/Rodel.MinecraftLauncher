namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Exercises the CLI boundary for the current fixed packaged-extension policy; arbitrary third-party registration is no longer supported.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ExtensionWorkflowTests
{
    /// <summary>
    /// Replaces obsolete registration/disabling workflows with the supported contract: legacy entries cannot launch or be changed through CLI.
    /// Actual command dispatch must reject mutation, project an empty external catalog and preserve original storage bytes.
    /// </summary>
    [TestMethod]
    [Timeout(30000, CooperativeCancellation = true)]
    public async Task FixedExtensions_RejectCustomRegistration_AndPreserveLegacyStorage()
    {
        await using var session = await AssistantCliSession.StartAsync();
        var directory = Directory.CreateDirectory(Path.Combine(session.Cli.Root, "ai")).FullName;
        var file = Path.Combine(directory, "extensions.json");
        const string legacy = """{"schemaVersion":1,"revision":7,"mcp":[{"id":"old","enabled":true,"executablePath":"must-not-run"}],"skillDirectories":[],"fileRoots":[],"future":"retain"}""";
        await File.WriteAllTextAsync(file, legacy, TestContext.CancellationToken);
        var request = Path.Combine(session.Cli.Root, "registration.json");
        await File.WriteAllTextAsync(request, """{"expectedRevision":7,"displayName":"blocked","executablePath":"must-not-run"}""", TestContext.CancellationToken);
        var saved = (await session.Cli.RunAsync("ai", "mcp", "save", "--file", request)).ReadEnvelope(1);
        Assert.AreEqual("AI_BUILTIN_EXTENSIONS_ONLY", saved["error"]!["code"]!.ToString());
        var disabled = (await session.Cli.RunAsync("ai", "mcp", "disable", "old", "--expected-revision", "7")).ReadEnvelope(1);
        Assert.AreEqual("AI_BUILTIN_EXTENSIONS_ONLY", disabled["error"]!["code"]!.ToString());
        var listed = (await session.Cli.RunAsync("ai", "mcp", "list")).ReadEnvelope(0)["data"]!;
        Assert.IsEmpty(listed["mcp"]!.AsArray());
        Assert.AreEqual(legacy, await File.ReadAllTextAsync(file, TestContext.CancellationToken));
    }

    /// <summary>Gets or sets cancellation for the isolated CLI owner and storage.</summary>
    public TestContext TestContext { get; set; }
}