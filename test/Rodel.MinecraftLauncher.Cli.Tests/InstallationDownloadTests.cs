using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects dependency-download scheduling and recovery through real CLI/Core and loopback HTTP.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class InstallationDownloadTests
{
    /// <summary>
    /// A held HTTP first wave proves actual overlap, not just task creation; the configured limit must
    /// also hold when set to one. Existing pack tests do not exercise vanilla library scheduling.
    /// Independently expected payloads and a second repair protect integrity and verified-file reuse.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public async Task Install_ConfiguredConcurrency_OverlapsWithinLimitAndRepairReusesFiles(int concurrency)
    {
        using var cli = new CliSandbox();
        await using var endpoint = await InstallationEndpoint.StartAsync(concurrency, TestContext.CancellationToken);
        var (id, runtime) = await PrepareAsync(cli, endpoint, concurrency);
        var installation = CliSandbox.RunAsync(cli.CreateStartInfo("install", id), cancellationToken: TestContext.CancellationToken);
        try
        {
            await WaitForFirstWaveAsync(endpoint, installation);
            Assert.AreEqual(concurrency, endpoint.Peak, "Independent HTTP responses must overlap at the configured limit.");
        }
        finally { endpoint.Release(); }

        AssertCompleted((await installation).ReadEnvelope(0)["data"]!);
        Assert.AreEqual(concurrency, endpoint.Peak, "No later wave may exceed the configured limit.");
        Assert.AreEqual(7, endpoint.Requests);
        AssertPayloads(cli.Root, runtime);

        AssertCompleted((await cli.RunAsync("repair", id)).ReadEnvelope(0)["data"]!);
        Assert.AreEqual(7, endpoint.Requests, "Verified files must not be downloaded again.");
        AssertPayloads(cli.Root, runtime);
    }

    /// <summary>
    /// A permanent 404 is released only after another library has written a partial file while its body is stalled.
    /// A server-side flush alone does not prove that the client started reading. Installation
    /// must cancel that sibling rather than wait for its network timeout, retain partial work without
    /// declaring completion, and recover through the ordinary repair command once the source is fixed.
    /// </summary>
    [TestMethod]
    public async Task Install_PermanentFailure_CancelsStalledSiblingAndRepairRecovers()
    {
        using var cli = new CliSandbox();
        await using var endpoint = await InstallationEndpoint.StartAsync(3, TestContext.CancellationToken);
        endpoint.FailLibrary = true;
        var (id, runtime) = await PrepareAsync(cli, endpoint, 3);
        var installation = CliSandbox.RunAsync(cli.CreateStartInfo("install", id), timeout: TimeSpan.FromSeconds(10),
            cancellationToken: TestContext.CancellationToken);
        try { await WaitForFirstWaveAsync(endpoint, installation); }
        finally { endpoint.Release(); }

        try
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            while (!Directory.EnumerateFiles(cli.Root, "library1.jar.rodel.partial", SearchOption.AllDirectories).Any(path => new FileInfo(path).Length > 0))
                await Task.Delay(20, deadline.Token);
        }
        finally { endpoint.AllowFailure(); }

        (await installation).ReadEnvelope(1);
        await endpoint.BodyAborted.WaitAsync(TimeSpan.FromSeconds(5), TestContext.CancellationToken);
        var journal = JsonNode.Parse(await File.ReadAllTextAsync(
            Path.Combine(runtime, ".rodel", "install", "installation-fixture.json"), TestContext.CancellationToken))!;
        Assert.AreEqual("preparing", journal["state"]?.ToString(), "Failed batches must not publish a completed installation.");
        Assert.IsTrue(Directory.EnumerateFiles(cli.Root, "*.rodel.partial", SearchOption.AllDirectories).Any(),
            "Canceled transfers must retain recoverable partial work.");

        endpoint.FailLibrary = false;
        AssertCompleted((await cli.RunAsync("repair", id)).ReadEnvelope(0)["data"]!);
        AssertPayloads(cli.Root, runtime);
        Assert.IsFalse(Directory.EnumerateFiles(cli.Root, "*.rodel.partial", SearchOption.AllDirectories).Any());
    }

    private async Task<(string Id, string Runtime)> PrepareAsync(CliSandbox cli, InstallationEndpoint endpoint, int concurrency)
    {
        (await cli.RunAsync("config", "set", "network", $$"""{"concurrency":{{concurrency}},"proxy":"direct"}""")).ReadEnvelope(0);
        var instance = (await cli.RunAsync("instance", "create", "installation-fixture", "--version", "installation-fixture")).ReadEnvelope(0)["data"]!;
        var runtime = instance["minecraftDirectory"]!.GetValue<string>();
        var directory = Path.Combine(runtime, "versions", "installation-fixture");
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, "installation-fixture.json"), endpoint.Metadata().ToJsonString(), TestContext.CancellationToken);
        return (instance["id"]!.GetValue<string>(), runtime);
    }

    private async Task WaitForFirstWaveAsync(InstallationEndpoint endpoint, Task<CliResult> installation)
    {
        // Startup latency is not download concurrency. Observe actual HTTP admission or the CLI's
        // own bounded termination, retaining its failure output instead of racing a second 5s timer.
        try
        {
            await Task.WhenAny(endpoint.FirstWave.Task, installation).WaitAsync(TestContext.CancellationToken);
            if (!endpoint.FirstWave.Task.IsCompletedSuccessfully)
            {
                var result = await installation;
                Assert.Fail($"CLI exited before the held HTTP wave arrived. Exit: {result.ExitCode}; stdout: {result.Output}; stderr: {result.Error}");
            }
        }
        catch
        {
            endpoint.Release();
            endpoint.AllowFailure();
            // Do not let a failed readiness assertion leave the child using the sandbox during disposal.
            await installation;
            throw;
        }
    }

    private static void AssertCompleted(JsonNode result)
    {
        Assert.AreEqual(7, result["completedFiles"]?.GetValue<int>());
        Assert.AreEqual(7, result["totalFiles"]?.GetValue<int>());
    }

    private static void AssertPayloads(string root, string runtime)
    {
        var references = JsonNode.Parse(File.ReadAllText(Path.Combine(runtime, "dependencies.json")))!;
        var dependencies = Path.Combine(root, "dependencies");
        var client = Path.Combine(dependencies, "clients", references["clients"]!["installation-fixture"] + ".jar");
        CollectionAssert.AreEqual(InstallationEndpoint.Payload("client.jar"), File.ReadAllBytes(client));
        var libraries = Path.Combine(dependencies, "libraries", references["libraries"]!.GetValue<string>(), "fixture");
        foreach (var name in new[] { "library0.jar", "library1.jar", "library2.jar", "library3.jar", "native.jar" })
            CollectionAssert.AreEqual(InstallationEndpoint.Payload(name), File.ReadAllBytes(Path.Combine(libraries, name)));
        CollectionAssert.AreEqual(InstallationEndpoint.Payload("logging.xml"), File.ReadAllBytes(
            Path.Combine(dependencies, "assets", references["assets"]!.GetValue<string>(), "log_configs", "logging.xml")));
    }

    /// <summary>Gets or sets the runner deadline used by the isolated HTTP and CLI workflow.</summary>
    public TestContext TestContext { get; set; } = null!;
}