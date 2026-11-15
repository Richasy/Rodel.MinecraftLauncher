using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects HMCL archive integrity, source-independent completion and frozen distribution updates over real loopback HTTP.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class HmclPackTests
{
    /// <summary>
    /// Accept the documented exporter shape and preserve unknown declarations without treating them as trust.
    /// Wrong embedded bytes must not register another client or alter the valid one. MRPACK completion tests
    /// cannot detect mistakenly extracting HMCL overrides after checksum validation or skipping its hash.
    /// Missing, unusable and non-string optional file APIs must not gate verified embedded bytes; retaining
    /// size, environment and extension fields catches lossy normalization rather than merely testing getters.
    /// </summary>
    [TestMethod]
    [DataRow("missing")]
    [DataRow("non-http")]
    [DataRow("non-string")]
    public async Task Import_EmbeddedFilesAndUnknownFields_VerifiesBytesBeforeRegistration(string source)
    {
        using var cli = new CliSandbox();
        var files = new Dictionary<string, string> { ["config/sample.txt"] = "exported content" };
        var manifest = Manifest(string.Empty, "one", files);
        if (source == "missing") manifest.Remove("fileApi");
        else if (source == "non-http") manifest["fileApi"] = "file:///unavailable";
        else manifest["fileApi"] = new JsonObject { ["unsupported"] = true };
        manifest["future"] = new JsonObject { ["value"] = 7 };
        var declaration = manifest["files"]![0]!;
        declaration["fileSize"] = Encoding.UTF8.GetByteCount("exported content");
        declaration["env"] = new JsonObject { ["client"] = "required", ["future"] = null };
        declaration["future"] = new JsonObject { ["retained"] = 19 };
        manifest["addons"]!.AsArray().Add(new JsonObject { ["id"] = "unknown-addon", ["version"] = "future" });
        var valid = WriteArchive(cli.Root, "valid.zip", manifest, files);

        var imported = (await cli.RunAsync("pack", "import", valid, "--offline")).ReadEnvelope(0)["data"]!;
        var id = imported["instance"]!["id"]!.GetValue<string>();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        var game = client.Instances.Get(id).GameDirectory;
        Assert.IsTrue(imported["complete"]!.GetValue<bool>());
        Assert.AreEqual("hmcl", imported["pack"]?["format"]?.ToString());
        Assert.AreEqual(7, imported["pack"]?["manifest"]?["future"]?["value"]?.GetValue<int>());
        Assert.AreEqual("future", imported["dependencies"]?["hmcl-addon:unknown-addon"]?.ToString());
        var retained = imported["pack"]!["files"]![0]!["metadata"]!;
        Assert.AreEqual(Encoding.UTF8.GetByteCount("exported content"), retained["fileSize"]!.GetValue<int>());
        Assert.IsTrue(JsonNode.DeepEquals(declaration["env"], retained["env"]));
        Assert.IsTrue(JsonNode.DeepEquals(declaration["future"], retained["future"]));
        Assert.AreEqual("exported content", await File.ReadAllTextAsync(Path.Combine(game, "config", "sample.txt"), TestContext.CancellationToken));

        var wrong = WriteArchive(cli.Root, "wrong.zip", manifest, new() { ["config/sample.txt"] = "wrong bytes" });
        (await cli.RunAsync("pack", "import", wrong, "--offline")).ReadEnvelope(1);
        Assert.HasCount(1, client.Instances.List());
        Assert.AreEqual("exported content", await File.ReadAllTextAsync(Path.Combine(game, "config", "sample.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// HMCL completion must use the persisted original declaration after the entry ZIP disappears, without
    /// re-importing or silently switching releases. This recovery differs from MRPACK's required source ZIP.
    /// </summary>
    [TestMethod]
    public async Task Complete_OriginalArchiveRemoved_UsesSavedManifestAndRetainsInstance()
    {
        using var cli = new CliSandbox();
        var files = new Dictionary<string, string> { ["config/required.txt"] = "local completion" };
        var manifest = Manifest(string.Empty, "one", files);
        var archive = WriteArchive(cli.Root, "source.zip", manifest, []);
        var imported = (await cli.RunAsync("pack", "import", archive, "--offline")).ReadEnvelope(0)["data"]!;
        var id = imported["instance"]!["id"]!.GetValue<string>();
        Assert.HasCount(1, imported["missing"]!.AsArray());
        File.Delete(archive);
        var manual = Path.Combine(cli.Root, "manual", "config");
        Directory.CreateDirectory(manual);
        await File.WriteAllTextAsync(Path.Combine(manual, "required.txt"), "local completion", TestContext.CancellationToken);

        var completed = (await cli.RunAsync("pack", "complete", id, "--offline", "--manual-directory", Path.GetDirectoryName(manual)!)).ReadEnvelope(0)["data"]!;

        Assert.IsTrue(completed["complete"]!.GetValue<bool>());
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        Assert.HasCount(1, client.Instances.List());
        Assert.IsTrue(JsonNode.DeepEquals(manifest, client.Packs.GetState(id).Pack!.Manifest));
        Assert.AreEqual("local completion", await File.ReadAllTextAsync(Path.Combine(client.Instances.Get(id).GameDirectory, "config", "required.txt"), TestContext.CancellationToken));
    }

    /// <summary>
    /// One frozen update restores managed removals, preserves player edits/extra files and disabled mods,
    /// and removes unchanged retired files. Stop the actual HTTP server before apply: querying latest or
    /// downloading again cannot pass. This is the HMCL policy boundary, not another generic apply test.
    /// </summary>
    [TestMethod]
    public async Task CheckThenApply_ServerUnavailable_UsesFrozenReleaseAndProtectsPlayerChoices()
    {
        using var cli = new CliSandbox();
        await using var server = new PackHttpServer();
        var original = new Dictionary<string, string>
        {
            ["config/restore.txt"] = "restore",
            ["config/player.txt"] = "old player",
            ["config/retired.txt"] = "retired",
            ["mods/disabled.jar"] = "old disabled"
        };
        var (client, id, game) = await ImportAsync(cli, Manifest(server.Root.AbsoluteUri, "one", original), original);
        File.Delete(Path.Combine(game, "config", "restore.txt"));
        await File.WriteAllTextAsync(Path.Combine(game, "config", "player.txt"), "player edit", TestContext.CancellationToken);
        File.Move(Path.Combine(game, "mods", "disabled.jar"), Path.Combine(game, "mods", "disabled.jar.disabled"));
        await File.WriteAllTextAsync(Path.Combine(game, "extra.txt"), "extra", TestContext.CancellationToken);
        var incoming = new Dictionary<string, string>
        {
            ["config/restore.txt"] = "restore",
            ["config/player.txt"] = "new player",
            ["config/added.txt"] = "added",
            ["mods/disabled.jar"] = "new disabled"
        };
        server.Set("server-manifest.json", Manifest(server.Root.AbsoluteUri, "two", incoming).ToJsonString());
        foreach (var (path, bytes) in incoming.Where(pair => pair.Key != "mods/disabled.jar"))
            server.Set("overrides/" + path, bytes);
        var review = (await cli.RunAsync("pack", "check", id)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(review["incoming"]!["complete"]!.GetValue<bool>(), review.ToJsonString());
        Assert.HasCount(1, review["incoming"]!["skipped"]!.AsArray());
        await server.DisposeAsync();

        var applied = (await cli.RunAsync("pack", "apply", review["operationId"]!.GetValue<string>(), "--content-only")).ReadEnvelope(0)["data"]!;

        Assert.AreEqual("two", client.Packs.GetState(id).Pack?.Manifest["version"]?.ToString());
        Assert.AreEqual("restore", await File.ReadAllTextAsync(Path.Combine(game, "config", "restore.txt"), TestContext.CancellationToken));
        Assert.AreEqual("added", await File.ReadAllTextAsync(Path.Combine(game, "config", "added.txt"), TestContext.CancellationToken));
        Assert.AreEqual("player edit", await File.ReadAllTextAsync(Path.Combine(game, "config", "player.txt"), TestContext.CancellationToken));
        Assert.AreEqual("old disabled", await File.ReadAllTextAsync(Path.Combine(game, "mods", "disabled.jar.disabled"), TestContext.CancellationToken));
        Assert.IsFalse(File.Exists(Path.Combine(game, "mods", "disabled.jar")));
        Assert.IsFalse(File.Exists(Path.Combine(game, "config", "retired.txt")));
        Assert.AreEqual("extra", await File.ReadAllTextAsync(Path.Combine(game, "extra.txt"), TestContext.CancellationToken));
        Assert.IsFalse(applied["consistency"]!["matchesRelease"]!.GetValue<bool>());
    }

    /// <summary>
    /// A half-published distribution must not modify a working baseline. Publishing the missing payload and
    /// checking again must produce an applicable new review, not require another import or accept old bytes.
    /// </summary>
    [TestMethod]
    public async Task Check_HalfPublishedRelease_RejectsApplyThenRecoversAfterPublication()
    {
        using var cli = new CliSandbox();
        await using var server = new PackHttpServer();
        var original = new Dictionary<string, string> { ["config/version.txt"] = "old" };
        var (client, id, game) = await ImportAsync(cli, Manifest(server.Root.AbsoluteUri, "one", original), original);
        var next = new Dictionary<string, string> { ["config/version.txt"] = "new", ["config/not-published.txt"] = "published later" };
        server.Set("server-manifest.json", Manifest(server.Root.AbsoluteUri, "two", next).ToJsonString());
        server.Set("overrides/config/version.txt", "new");
        var incomplete = (await cli.RunAsync("pack", "check", id)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(incomplete["incoming"]!["complete"]!.GetValue<bool>());

        (await cli.RunAsync("pack", "apply", incomplete["operationId"]!.GetValue<string>(), "--content-only")).ReadEnvelope(1);
        Assert.AreEqual("one", client.Packs.GetState(id).Pack?.Manifest["version"]?.ToString());
        Assert.AreEqual("old", await File.ReadAllTextAsync(Path.Combine(game, "config", "version.txt"), TestContext.CancellationToken));
        server.Set("overrides/config/not-published.txt", "published later");
        var ready = (await cli.RunAsync("pack", "check", id)).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(ready["incoming"]!["complete"]!.GetValue<bool>());
        (await cli.RunAsync("pack", "apply", ready["operationId"]!.GetValue<string>(), "--content-only")).ReadEnvelope(0);

        Assert.HasCount(1, client.Instances.List());
        Assert.AreEqual("two", client.Packs.GetState(id).Pack?.Manifest["version"]?.ToString());
        Assert.AreEqual("published later", await File.ReadAllTextAsync(Path.Combine(game, "config", "not-published.txt"), TestContext.CancellationToken));
        var unchanged = (await cli.RunAsync("pack", "check", id)).ReadEnvelope(0)["data"]!;
        Assert.IsFalse(unchanged["hasChanges"]!.GetValue<bool>(), "The repaired release must not be offered as another applicable update.");
    }

    private static async Task<(LauncherClient Client, string Id, string Game)> ImportAsync(CliSandbox cli, JsonObject manifest, Dictionary<string, string> files)
    {
        var archive = WriteArchive(cli.Root, "original.zip", manifest, files);
        var imported = (await cli.RunAsync("pack", "import", archive, "--offline")).ReadEnvelope(0)["data"]!;
        var id = imported["instance"]!["id"]!.GetValue<string>();
        var client = new LauncherClient(new LauncherPaths(cli.Root));
        return (client, id, client.Instances.Get(id).GameDirectory);
    }

    /// <summary>
    /// Detachment must suppress source access, not erase provenance or player files. Reattaching the same
    /// release is a real reviewable metadata change even when no bytes differ; a generic no-op decision
    /// would otherwise make source recovery impossible. No external service or game is involved.
    /// </summary>
    [TestMethod]
    public async Task Detach_ExplicitArchiveReattachment_PreservesFilesAndRestoresSource()
    {
        using var cli = new CliSandbox();
        await using var server = new PackHttpServer();
        var files = new Dictionary<string, string> { ["config/retained.txt"] = "retained" };
        var manifest = Manifest(server.Root.AbsoluteUri, "one", files);
        var (client, id, game) = await ImportAsync(cli, manifest, files);
        await server.DisposeAsync();

        (await cli.RunAsync("pack", "detach", id)).ReadEnvelope(0);
        Assert.IsTrue(client.Packs.GetState(id).Pack!.SourceDetached);
        var blocked = (await cli.RunAsync("pack", "check", id)).ReadEnvelope(1);
        Assert.AreEqual("OPERATION_FAILED", blocked["error"]?["code"]?.ToString(), "Detached checks must reject before contacting the stopped HTTP endpoint.");
        Assert.IsTrue(JsonNode.DeepEquals(manifest, client.Packs.GetState(id).Pack!.Manifest));
        var archive = WriteArchive(cli.Root, "reattach.zip", manifest, files);
        var review = (await cli.RunAsync("pack", "review", id, archive, "--offline")).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(review["hasChanges"]!.GetValue<bool>());
        (await cli.RunAsync("pack", "apply", review["operationId"]!.GetValue<string>(), "--content-only")).ReadEnvelope(0);

        Assert.IsNull(client.Packs.GetState(id).Pack!.SourceDetached);
        Assert.HasCount(1, client.Instances.List());
        Assert.AreEqual("retained", await File.ReadAllTextAsync(Path.Combine(game, "config", "retained.txt"), TestContext.CancellationToken));
    }

    private static JsonObject Manifest(string api, string version, Dictionary<string, string> files) => new()
    {
        ["name"] = "HMCL interoperability fixture",
        ["version"] = version,
        ["fileApi"] = api,
        ["addons"] = new JsonArray(new JsonObject { ["id"] = "game", ["version"] = "fixture-version" }),
        ["files"] = new JsonArray([.. files.Select(pair => (JsonNode)new JsonObject
        {
            ["path"] = pair.Key,
            ["hash"] = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(pair.Value))).ToLowerInvariant()
        })])
    };

    private static string WriteArchive(string root, string name, JsonObject manifest, Dictionary<string, string> files)
    {
        Directory.CreateDirectory(root);
        var path = Path.Combine(root, name);
        using var output = File.Create(path);
        using var zip = new ZipArchive(output, ZipArchiveMode.Create);
        WriteEntry(zip, "server-manifest.json", manifest.ToJsonString());
        foreach (var (relative, bytes) in files)
            WriteEntry(zip, "overrides/" + relative, bytes);
        return path;
    }

    private static void WriteEntry(ZipArchive zip, string path, string content)
    {
        using var writer = new StreamWriter(zip.CreateEntry(path).Open());
        writer.Write(content);
    }

    /// <summary>Supplies the runner-owned deadline to isolated file and process operations.</summary>
    public TestContext TestContext { get; set; } = null!;
}