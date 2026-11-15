using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Diagnostics;
using System.IO.Compression;
using System.Text;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects actual visual-pack selection, unrelated player configuration and optimistic concurrency across real files.</summary>
[TestClass]
public sealed class VisualPackSelectionTests
{
    /// <summary>
    /// Review: inventory enable tests cannot detect missing game selection. This real Core lifecycle distinguishes
    /// available bytes from use, preserves unknown/built-in positions, changes priority, and rejects stale UI choices
    /// before restoring a disabled file. No game or external network is substituted for a claimed render result.
    /// </summary>
    [TestMethod]
    public Task ResourceUsage_ChangesRealSelectionAndPriority_WithoutLosingOtherChoices()
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("visual-pack-fixture", "26.3", ct);
            var game = instance.GameDirectory;
            Directory.CreateDirectory(Path.Combine(game, "resourcepacks"));
            WriteArchive(Path.Combine(game, "resourcepacks", "a.zip"), "pack.mcmeta", """{"pack":{"pack_format":1,"description":"a"}}""");
            WriteArchive(Path.Combine(game, "resourcepacks", "b.zip.disabled"), "pack.mcmeta", """{"pack":{"pack_format":1,"description":"b"}}""");
            var options = Path.Combine(game, "options.txt");
            await File.WriteAllTextAsync(options, "resourcePacks:[\"vanilla\",\"file/a.zip\",\"mod:retained\"]\nincompatibleResourcePacks:[]\nfutureOption:retained\n", ct);
            var before = await client.GetResourcePacksAsync(instance.Id, cancellationToken: ct);

            await client.SetResourcePackUsageAsync(instance.Id, "b.zip.disabled", true, before.Fingerprint, ct);
            var selected = await client.GetResourcePacksAsync(instance.Id, cancellationToken: ct);
            CollectionAssert.AreEqual(InitialSelection, selected.Selected.Select(value => value!.ToString()).ToArray());
            Assert.IsTrue(File.Exists(Path.Combine(game, "resourcepacks", "b.zip")));
            await client.SetResourcePackUsageAsync(instance.Id, "a.zip", true, selected.Fingerprint, ct);
            var repeated = await client.GetResourcePacksAsync(instance.Id, cancellationToken: ct);
            CollectionAssert.AreEqual(selected.Selected.Select(value => value!.ToString()).ToArray(), repeated.Selected.Select(value => value!.ToString()).ToArray(),
                "Using an already selected pack must not silently change its priority.");
            selected = repeated;
            await client.MoveResourcePackAsync(instance.Id, "a.zip", true, selected.Fingerprint, ct);
            selected = await client.GetResourcePacksAsync(instance.Id, cancellationToken: ct);
            CollectionAssert.AreEqual(ReorderedSelection, selected.Selected.Select(value => value!.ToString()).ToArray());
            await client.SetResourcePackUsageAsync(instance.Id, "b.zip", false, selected.Fingerprint, ct);
            Assert.IsTrue(File.Exists(Path.Combine(game, "resourcepacks", "b.zip")), "Stopping use must keep the installed archive available.");
            Assert.Contains("futureOption:retained", await File.ReadAllTextAsync(options, ct));

            await client.Content.SetEnabledAsync(game, "resourcepacks", "b.zip", false, ct: ct);
            selected = await client.GetResourcePacksAsync(instance.Id, cancellationToken: ct);
            await File.AppendAllTextAsync(options, "externalEdit:keep\n", ct);
            var external = await File.ReadAllBytesAsync(options, ct);
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => client.SetResourcePackUsageAsync(instance.Id, "b.zip.disabled", true, selected.Fingerprint, ct));
            Assert.AreEqual("GAME_SETTINGS_CHANGED", failure.Code);
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(options, ct));
            Assert.IsTrue(File.Exists(Path.Combine(game, "resourcepacks", "b.zip.disabled")));
        });

    /// <summary>
    /// Review: these independent Java-properties fixtures protect Unicode names, comments, duplicate/continued keys,
    /// ISO-8859-1 bytes and actual single-pack replacement. Raw escaped output and exact backup bytes are independent
    /// oracles, rather than relying only on the reader paired with the writer. Planning cannot restore disabled files.
    /// </summary>
    /// <param name="engine">The actual configuration format owner whose key semantics are exercised.</param>
    [TestMethod]
    [DataRow("iris")]
    [DataRow("oculus")]
    [DataRow("optifine")]
    public Task ShaderUsage_PreservesJavaPropertiesAndBackup_AndRejectsStaleWrites(string engine)
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("shader-fixture", "26.3", ct);
            var game = instance.GameDirectory;
            Directory.CreateDirectory(Path.Combine(game, "shaderpacks"));
            WriteArchive(Path.Combine(game, "shaderpacks", "夜景.zip.disabled"), "shaders/fixture.fsh", "synthetic shader fixture");
            var config = engine == "optifine" ? Path.Combine(game, "optionsshaders.txt") : Path.Combine(game, "config", engine + ".properties");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            const string original = "# keep this comment \\\r\nenableShaders=false\r\nshaderPack=prior.zip\r\nshaderPack : \\\r\n old.zip\r\ncustom\\:key=caf\u00e9\r\nfutureSetting=kept\\ value\r\n";
            var originalBytes = Encoding.Latin1.GetBytes(original);
            await File.WriteAllBytesAsync(config, originalBytes, ct);
            var before = await client.GetShaderPacksAsync(instance.Id, cancellationToken: ct);
            Assert.AreEqual(engine, before.Engine);
            Assert.AreEqual("old.zip", before.Selected);
            Assert.AreEqual(engine == "optifine", before.Enabled);
            var planned = await client.SelectShaderPackAsync(instance.Id, new() { FileName = "夜景.zip", PlanOnly = true, ExpectedFingerprint = before.Fingerprint }, ct);
            Assert.AreEqual("planned", planned.State);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(config, ct));
            Assert.IsTrue(File.Exists(Path.Combine(game, "shaderpacks", "夜景.zip.disabled")));

            var applied = await client.SelectShaderPackAsync(instance.Id, new() { FileName = "夜景.zip", ExpectedFingerprint = before.Fingerprint }, ct);
            Assert.AreEqual("applied", applied.State);
            Assert.AreEqual("夜景.zip", applied.Selection.Selected);
            Assert.IsTrue(applied.Selection.Enabled && applied.Selection.Present);
            CollectionAssert.AreEqual(originalBytes, await File.ReadAllBytesAsync(applied.RecoveryPath!, ct));
            var published = Encoding.Latin1.GetString(await File.ReadAllBytesAsync(config, ct));
            Assert.Contains("shaderPack=\\u591c\\u666f.zip", published);
            Assert.Contains("custom\\:key=caf\u00e9\r\nfutureSetting=kept\\ value\r\n", published);
            Assert.Contains("# keep this comment \\\r\n", published);
            var disabled = await client.SelectShaderPackAsync(instance.Id, new() { ExpectedFingerprint = applied.Selection.Fingerprint }, ct);
            Assert.IsFalse(disabled.Selection.Enabled);
            Assert.IsNull(disabled.Selection.Selected);
            Assert.IsTrue(File.Exists(Path.Combine(game, "shaderpacks", "夜景.zip")));

            await File.AppendAllTextAsync(config, "externalEdit=retained\r\n", Encoding.Latin1, ct);
            var external = await File.ReadAllBytesAsync(config, ct);
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => client.SelectShaderPackAsync(instance.Id,
                new() { FileName = "夜景.zip", ExpectedFingerprint = disabled.Selection.Fingerprint }, ct));
            Assert.AreEqual("SHADER_CONFIG_CHANGED", failure.Code);
            CollectionAssert.AreEqual(external, await File.ReadAllBytesAsync(config, ct));
        });

    /// <summary>A genuine live process identity in session storage must block configuration writes, not merely disable an App button.</summary>
    [TestMethod]
    public Task ShaderUsage_RefusesAnActiveInstance_WithoutEditingItsConfiguration()
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("busy-shader-fixture", "26.3", ct);
            var config = Path.Combine(instance.GameDirectory, "config", "iris.properties");
            Directory.CreateDirectory(Path.GetDirectoryName(config)!);
            await File.WriteAllTextAsync(config, "shaderPack=old.zip\nenableShaders=true\n", ct);
            using var owner = Process.GetCurrentProcess();
            var id = Guid.NewGuid().ToString("N");
            await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, id + ".json"), new System.Text.Json.Nodes.JsonObject
            {
                ["id"] = id,
                ["status"] = "running",
                ["instanceId"] = instance.Id,
                ["gameDirectory"] = instance.GameDirectory,
                ["processId"] = owner.Id,
                ["processStartUtc"] = owner.StartTime.ToUniversalTime().ToString("O")
            }, ct);
            var failure = await Assert.ThrowsAsync<LauncherOperationException>(() => client.SelectShaderPackAsync(instance.Id, new(), ct));
            Assert.AreEqual("INSTANCE_BUSY", failure.Code);
            Assert.AreEqual("game", failure.Details?["reason"]?.ToString());
            Assert.AreEqual("shaderPack=old.zip\nenableShaders=true\n", await File.ReadAllTextAsync(config, ct));
        });

    private static void WriteArchive(string path, string entry, string text)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry(entry).Open());
        writer.Write(text);
    }

    /// <summary>Gets or sets cooperative cancellation for each isolated lifecycle.</summary>
    public TestContext TestContext { get; set; }

    private static readonly string[] InitialSelection = ["vanilla", "file/a.zip", "mod:retained", "file/b.zip"];
    private static readonly string[] ReorderedSelection = ["vanilla", "file/b.zip", "mod:retained", "file/a.zip"];
}