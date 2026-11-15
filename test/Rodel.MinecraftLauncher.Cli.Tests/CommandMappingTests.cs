namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// Protects feature-owned option projection into durable typed requests, not parser primitives or job lifecycle.
/// Synthetic instances or missing runtimes stop execution before downloads or game processes can start.
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class CommandMappingTests
{
    /// <summary>
    /// Runtime execution and its background factory must preserve the same requested components and removal intent.
    /// </summary>
    [TestMethod]
    [DataRow("change", false)]
    [DataRow("remove-loader", true)]
    public async Task RuntimeRequest_ForegroundAndBackground_PreservesComponentChoices(string operation, bool removeLoader)
    {
        using var cli = new CliSandbox();
        var missing = Guid.NewGuid().ToString("N");
        var java = Path.Combine(cli.Root, "manual java", "java.exe");
        string[] arguments = ["runtime", operation, missing, "--minecraft", "1.21.1", "--version", "custom-profile", "--loader", "fabric", "--loader-version", "0.16.0", "--java", java, "--library-mirror", "https://mirror.invalid/repository/", "--offline", "--plan"];
        var foreground = (await cli.RunAsync(arguments)).ReadEnvelope(1);
        await using var job = await JobHost.StartAsync(cli, arguments);

        var terminal = await job.WaitForTerminalAsync();
        var request = terminal["request"]!["parameters"]!;
        var options = request["options"]!;

        Assert.AreEqual("failed", terminal["status"]?.GetValue<string>(), terminal.ToJsonString());
        Assert.AreEqual(foreground["error"]?["code"]?.GetValue<string>(), terminal["failure"]?["error"]?["code"]?.GetValue<string>());
        Assert.AreEqual("change-runtime", request["kind"]?.GetValue<string>());
        Assert.AreEqual(missing, request["instanceId"]?.GetValue<string>());
        Assert.AreEqual("1.21.1", options["minecraftVersion"]?.GetValue<string>());
        Assert.AreEqual("custom-profile", options["versionId"]?.GetValue<string>());
        Assert.AreEqual("fabric", options["loader"]?.GetValue<string>());
        Assert.AreEqual("0.16.0", options["loaderVersion"]?.GetValue<string>());
        Assert.AreEqual(java, options["javaPath"]?.GetValue<string>());
        Assert.AreEqual("https://mirror.invalid/repository/", options["libraryMirrors"]?[0]?.GetValue<string>());
        Assert.AreEqual(removeLoader, options["removeLoader"]?.GetValue<bool>());
        Assert.IsTrue(options["offline"]?.GetValue<bool>());
        Assert.IsTrue(options["planOnly"]?.GetValue<bool>());
    }

    private static readonly string[] expected = ["--demo", "space value"];

    /// <summary>
    /// Shared launch mapping retains repeated tokens, nullable false, environment changes and explicit ordinary
    /// launch for a dedicated client. Omitting no-server mapping would silently restore its inherited connection.
    /// </summary>
    [TestMethod]
    public async Task LaunchRequest_RepeatedAndNullableValues_PreservesTypedBackgroundOptions()
    {
        using var cli = new CliSandbox();
        var created = (await cli.RunAsync("instance", "create", "mapping-fixture", "--version", "missing-runtime")).ReadEnvelope(0);
        var instanceId = created["data"]!["id"]!.GetValue<string>();
        (await cli.RunAsync("server", "add", "mapping server", "127.0.0.1:25568", "--id", "mapping-server")).ReadEnvelope(0);
        (await cli.RunAsync("server", "bind", "mapping-server", instanceId, "--dedicated")).ReadEnvelope(0);
        string[] arguments = ["launch", instanceId, "--no-server", "--offline", "--plan", "--fullscreen=false", "--width", "1234", "--jvm-arg=-Dexample=space value", "--game-arg=--demo", "--game-arg=space value", "--env", "OVERRIDE=first", "--env", "OVERRIDE=second=value", "--env", "EMPTY=", "--env", "REMOVE"];
        var foreground = (await cli.RunAsync(arguments)).ReadEnvelope(1);
        await using var job = await JobHost.StartAsync(cli, arguments);

        var terminal = await job.WaitForTerminalAsync();
        var request = terminal["request"]!["parameters"]!;
        var options = request["options"]!;

        Assert.AreEqual("failed", terminal["status"]?.GetValue<string>(), terminal.ToJsonString());
        Assert.AreEqual(foreground["error"]?["code"]?.GetValue<string>(), terminal["failure"]?["error"]?["code"]?.GetValue<string>());
        Assert.AreEqual("launch", request["kind"]?.GetValue<string>());
        Assert.AreEqual(instanceId, request["instanceId"]?.GetValue<string>());
        Assert.IsFalse(options["inheritServer"]!.GetValue<bool>());
        Assert.IsNull(options["serverAddress"]);
        Assert.IsFalse(options["fullscreen"]?.GetValue<bool>());
        Assert.AreEqual(1234, options["width"]?.GetValue<int>());
        Assert.AreEqual("-Dexample=space value", options["jvmArguments"]?[0]?.GetValue<string>());
        CollectionAssert.AreEqual(expected, options["gameArguments"]!.AsArray().Select(item => item!.GetValue<string>()).ToArray());
        Assert.AreEqual("second=value", options["environment"]?["OVERRIDE"]?.GetValue<string>());
        Assert.AreEqual(string.Empty, options["environment"]?["EMPTY"]?.GetValue<string>());
        Assert.IsTrue(options["environment"]!.AsObject().ContainsKey("REMOVE"));
        Assert.IsNull(options["environment"]?["REMOVE"]);
        Assert.IsTrue(options["offline"]?.GetValue<bool>());
        Assert.IsTrue(options["planOnly"]?.GetValue<bool>());
    }
}