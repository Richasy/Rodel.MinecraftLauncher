using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>
/// 验证配置命令跨进程持久化及无关设置保护；不重复覆盖参数库的解析规则或所有配置键。
/// </summary>
[TestClass]
[TestCategory("Integration")]
public sealed class ConfigurationWorkflowTests
{
    /// <summary>
    /// 防止语言预解析或参数转发将用户文本误当成选项，并验证写入不会覆盖其他设置。
    /// </summary>
    [TestMethod]
    public async Task SetThenRead_OptionLikeUnicodeLiteral_PreservesValueAndUnrelatedSetting()
    {
        using var cli = new CliSandbox();
        const string literal = """--background --language=ar C:\游戏 saves "quoted" --name=literal""";

        // 无关设置作为哨兵，确保后续写入不是用新对象覆盖整份配置。
        var seed = await cli.RunAsync("config", "set", "unrelated", "{\"keep\":true}");
        seed.ReadEnvelope(0);

        // -- 后的值必须整体保留，即使其中包含本来有效的全局选项。
        var write = await cli.RunAsync("config", "set", "--", "reviewNote", literal);
        var written = write.ReadEnvelope(0);
        Assert.AreEqual(literal, written["data"]?["value"]?.GetValue<string>());

        // 用新进程及真实磁盘核对持久化，避免只验证写入命令自己的成功回显。
        var read = await cli.RunAsync("config", "get", "reviewNote");
        var saved = read.ReadEnvelope(0);
        var unrelated = (await cli.RunAsync("config", "get", "unrelated")).ReadEnvelope(0);

        Assert.AreEqual(literal, saved["data"]?["value"]?.GetValue<string>());
        Assert.AreEqual("global", saved["data"]?["source"]?.GetValue<string>());
        Assert.IsTrue(unrelated["data"]?["value"]?["keep"]?.GetValue<bool>());
        var persisted = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken));
        Assert.AreEqual(literal, persisted?["reviewNote"]?.GetValue<string>());
        Assert.IsTrue(persisted?["unrelated"]?["keep"]?.GetValue<bool>());
    }

    /// <summary>
    /// An instance override must not change global settings, another instance, or unrelated metadata.
    /// </summary>
    [TestMethod]
    public async Task SetInstanceOverride_IndependentScopes_PreservesGlobalAndOtherInstance()
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "sharedOption", "global")).ReadEnvelope(0);
        var first = (await cli.RunAsync("instance", "create", "first", "--version", "synthetic-version")).ReadEnvelope(0);
        var second = (await cli.RunAsync("instance", "create", "second", "--version", "synthetic-version")).ReadEnvelope(0);
        var firstId = first["data"]!["id"]!.GetValue<string>();
        var secondId = second["data"]!["id"]!.GetValue<string>();
        var firstFile = Path.Combine(cli.Root, "instances", firstId, "instance.json");
        var secondFile = Path.Combine(cli.Root, "instances", secondId, "instance.json");
        var metadata = JsonNode.Parse(await File.ReadAllTextAsync(firstFile, TestContext.CancellationToken))!;
        metadata["extension"] = new JsonObject { ["keep"] = true };
        await File.WriteAllTextAsync(firstFile, metadata.ToJsonString(), TestContext.CancellationToken);
        var globalBefore = await File.ReadAllBytesAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken);
        var secondBefore = await File.ReadAllBytesAsync(secondFile, TestContext.CancellationToken);

        var written = (await cli.RunAsync("config", "set", "sharedOption", "instance", "--instance", firstId)).ReadEnvelope(0);
        var inherited = (await cli.RunAsync("config", "get", "sharedOption", "--instance", secondId)).ReadEnvelope(0);
        var reopened = (await cli.RunAsync("config", "get", "sharedOption", "--instance", firstId)).ReadEnvelope(0);

        Assert.AreEqual("instance", written["data"]?["source"]?.GetValue<string>());
        Assert.AreEqual("instance", reopened["data"]?["value"]?.GetValue<string>());
        Assert.AreEqual("global", inherited["data"]?["source"]?.GetValue<string>());
        Assert.AreEqual("global", inherited["data"]?["value"]?.GetValue<string>());
        CollectionAssert.AreEqual(globalBefore, await File.ReadAllBytesAsync(Path.Combine(cli.Root, "settings.json"), TestContext.CancellationToken));
        CollectionAssert.AreEqual(secondBefore, await File.ReadAllBytesAsync(secondFile, TestContext.CancellationToken));
        Assert.IsTrue(JsonNode.Parse(await File.ReadAllTextAsync(firstFile, TestContext.CancellationToken))?["extension"]?["keep"]?.GetValue<bool>());
    }

    /// <summary>
    /// A real replacement failure must preserve the original file; releasing the handle must allow retry.
    /// </summary>
    [TestMethod]
    [OSCondition(OperatingSystems.Windows)]
    public async Task SetWhileFileIsLocked_ReleaseAndRetry_PreservesDataAndRecovers()
    {
        using var cli = new CliSandbox();
        (await cli.RunAsync("config", "set", "extension", "{\"keep\":true}")).ReadEnvelope(0);
        var file = Path.Combine(cli.Root, "settings.json");
        var original = await File.ReadAllBytesAsync(file, TestContext.CancellationToken);

        // Reading remains possible, but denying shared-delete access prevents atomic replacement.
        using (var locked = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = (await cli.RunAsync("config", "set", "reviewNote", "updated")).ReadEnvelope(1);
            Assert.AreEqual("IO_ERROR", failure["error"]?["code"]?.GetValue<string>());
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(file, TestContext.CancellationToken));
            Assert.IsEmpty(Directory.GetFiles(cli.Root, "*.tmp", SearchOption.AllDirectories));
        }

        (await cli.RunAsync("config", "set", "reviewNote", "updated")).ReadEnvelope(0);
        var reopened = (await cli.RunAsync("config", "get", "reviewNote")).ReadEnvelope(0);
        Assert.AreEqual("updated", reopened["data"]?["value"]?.GetValue<string>());
        Assert.IsTrue(JsonNode.Parse(await File.ReadAllTextAsync(file, TestContext.CancellationToken))?["extension"]?["keep"]?.GetValue<bool>());
    }

    /// <summary>Gets or sets the MSTest context supplying this test's cancellation token.</summary>
    public TestContext TestContext { get; set; }
}