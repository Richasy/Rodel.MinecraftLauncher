namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Protects execution and queueing boundaries; tests project-specific validation, not parser-library primitives.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CommandValidationTests
{
    /// <summary>
    /// Invalid write requests must be rejected before changing existing files or persisting a background job.
    /// Each row exercises a different application-level guard.
    /// </summary>
    [TestMethod]
    [DataRow(["content", "add", "unused-instance", "missing.jar", "--plan", "--background"])]
    [DataRow(["content", "apply-review", "unused-instance", "unused-review", "--background"])]
    [DataRow(["content", "repair", "unused-instance", "--review", "unused-review", "--session", "unused-session", "--confirm", "--background"])]
    [DataRow(["instance", "create", "must-not-exist", "--background"])]
    public async Task RejectedWrite_BeforeExecution_PreservesFilesAndDoesNotQueue(string[] arguments)
    {
        using var cli = new CliSandbox();
        Directory.CreateDirectory(cli.Root);
        await File.WriteAllTextAsync(Path.Combine(cli.Root, "settings.json"), """{"sentinel":{"keep":true}}""", TestContext.CancellationToken);
        var original = cli.SnapshotFiles();

        var failure = (await cli.RunAsync(arguments)).ReadEnvelope(2);

        Assert.AreEqual("failed", failure["status"]?.GetValue<string>());
        Assert.AreEqual("INVALID_ARGUMENT", failure["error"]?["code"]?.GetValue<string>());
        var after = cli.SnapshotFiles();
        CollectionAssert.AreEquivalent(original.Keys.ToArray(), after.Keys.ToArray(), "Validation created or removed files.");
        foreach (var path in original.Keys)
            Assert.AreEqual(original[path], after[path], $"Validation changed {path}.");
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}