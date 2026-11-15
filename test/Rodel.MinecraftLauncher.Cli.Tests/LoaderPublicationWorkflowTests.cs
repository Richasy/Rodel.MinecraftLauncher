using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects loader publication and recovery through actual CLI/Core and isolated cached files, without running Minecraft or a loader processor.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class LoaderPublicationWorkflowTests
{
    /// <summary>
    /// Review: Java imports do not exercise Loader's durable staging, previous-file backups or journal recovery.
    /// A real Windows handle permits backup reads but denies target replacement; releasing it must allow the same
    /// staged bytes to publish, retaining the original backup and extensible local profile. Reinstallation must reuse
    /// that result. The supported unmarked runtime layout keeps the target identity stable across vanilla preparation.
    /// Only the already-downloaded profile/library and vanilla client are synthetic; the CLI and I/O are real.
    /// </summary>
    [TestMethod]
    public async Task Install_LockedReplacement_PreservesOriginalAndRetryPublishesStagedLibrary()
    {
        using var cli = new CliSandbox();
        const string minecraft = "publication-fixture";
        const string loaderVersion = "fixture-loader";
        const string versionId = minecraft + "-fabric-" + loaderVersion;
        var instance = (await cli.RunAsync("instance", "create", "Loader publication", "--version", minecraft)).ReadEnvelope(0)["data"]!;
        var id = instance["id"]!.GetValue<string>();
        var runtime = instance["minecraftDirectory"]!.GetValue<string>();
        // Exercise the supported legacy layout: a shared dependency detach would move publication away from the held file.
        File.Delete(Path.Combine(runtime, SharedRuntimeLayout.FileName));
        await PrepareVanillaAsync(runtime, minecraft);
        (await cli.RunAsync("install", id)).ReadEnvelope(0);

        byte[] original = [11, 22, 33, 44];
        byte[] incoming = [55, 66, 77, 88, 99];
        var destination = Path.Combine(runtime, "libraries", "fixture", "library.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await File.WriteAllBytesAsync(destination, original, TestContext.CancellationToken);
        var work = Path.Combine(runtime, ".rodel", "loaders", versionId);
        var source = Path.Combine(work, "staging", "libraries", "fixture", "library.jar");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        await File.WriteAllBytesAsync(source, incoming, TestContext.CancellationToken);
        var profile = Path.Combine(runtime, "versions", versionId, versionId + ".json");
        Directory.CreateDirectory(Path.GetDirectoryName(profile)!);
        var metadata = new JsonObject
        {
            ["id"] = versionId,
            ["inheritsFrom"] = minecraft,
            ["mainClass"] = "fixture.Loader",
            ["libraries"] = new JsonArray(new JsonObject
            {
                ["name"] = "fixture:library:1",
                ["downloads"] = new JsonObject
                {
                    ["artifact"] = new JsonObject
                    {
                        ["path"] = "fixture/library.jar",
                        ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(incoming)),
                        ["size"] = incoming.Length,
                        ["url"] = string.Empty
                    }
                }
            }),
            ["futureExtension"] = new JsonObject { ["retain"] = true }
        };
        await File.WriteAllTextAsync(profile, metadata.ToJsonString(), TestContext.CancellationToken);
        var originalProfile = await File.ReadAllBytesAsync(profile, TestContext.CancellationToken);
        string[] install = ["loader", "install", id, "fabric", "--minecraft", minecraft, "--loader-version", loaderVersion];

        using (var held = new FileStream(destination, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            (await cli.RunAsync(install)).ReadEnvelope(1);
            CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
            var journal = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(work, "state.json"), TestContext.CancellationToken))!;
            Assert.AreEqual("failed", journal["state"]!.GetValue<string>());
            Assert.AreEqual("completed", journal["rollback"]!.GetValue<string>());
        }

        var result = (await cli.RunAsync(install)).ReadEnvelope(0)["data"]!;
        Assert.AreEqual(versionId, result["versionId"]!.GetValue<string>());
        Assert.AreEqual("local", result["profileSource"]!.GetValue<string>());
        CollectionAssert.AreEqual(incoming, await File.ReadAllBytesAsync(destination, TestContext.CancellationToken));
        CollectionAssert.AreEqual(incoming, await File.ReadAllBytesAsync(source, TestContext.CancellationToken));
        CollectionAssert.AreEqual(originalProfile, await File.ReadAllBytesAsync(profile, TestContext.CancellationToken));
        var backup = Directory.GetFiles(Path.Combine(work, "backups"), "*.bak").Single();
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(backup, TestContext.CancellationToken));
        Assert.IsEmpty(Directory.GetFiles(runtime, "*.tmp", SearchOption.AllDirectories));

        var retainedTime = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(destination, retainedTime);
        (await cli.RunAsync(install)).ReadEnvelope(0);
        Assert.AreEqual(retainedTime, File.GetLastWriteTimeUtc(destination), "Verified identical loader bytes must not be rewritten.");
        CollectionAssert.AreEqual(original, await File.ReadAllBytesAsync(backup, TestContext.CancellationToken));
    }

    private async Task PrepareVanillaAsync(string runtime, string version)
    {
        var directory = Path.Combine(runtime, "versions", version);
        Directory.CreateDirectory(directory);
        await File.WriteAllTextAsync(Path.Combine(directory, version + ".json"),
            new JsonObject { ["id"] = version, ["mainClass"] = "fixture.Main", ["libraries"] = new JsonArray() }.ToJsonString(), TestContext.CancellationToken);
        using var client = ZipFile.Open(Path.Combine(directory, version + ".jar"), ZipArchiveMode.Create);
    }

    /// <summary>Gets the runner cancellation signal for isolated fixture files.</summary>
    public TestContext TestContext { get; set; } = null!;
}