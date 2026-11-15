using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Protects pack-file scheduling and verified ownership through the real CLI, HTTP and isolated file store.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class PackAcquisitionTests
{
    /// <summary>
    /// Review: vanilla scheduling coverage cannot detect the pack path downloading serially or ignoring the
    /// configured limit. Holding actual HTTP admission proves the first wave; its peak remains bounded across
    /// later waves. Independently hashed payloads and declaration order also detect lost concurrent results.
    /// </summary>
    [TestMethod]
    [DataRow(1)]
    [DataRow(3)]
    public async Task Import_ConfiguredConcurrency_OverlapsAndRetainsVerifiedFiles(int concurrency)
    {
        using var cli = new CliSandbox();
        await using var endpoint = await InstallationEndpoint.StartAsync(concurrency, TestContext.CancellationToken);
        (await cli.RunAsync("config", "set", "network", $$"""{"concurrency":{{concurrency}},"proxy":"direct"}""")).ReadEnvelope(0);
        var archive = CreateArchive(cli.Root, endpoint.Root);
        var importing = CliSandbox.RunAsync(cli.CreateStartInfo("pack", "import", archive, "--content-only"), cancellationToken: TestContext.CancellationToken);
        try
        {
            await Task.WhenAny(endpoint.FirstWave.Task, importing).WaitAsync(TestContext.CancellationToken);
            Assert.IsTrue(endpoint.FirstWave.Task.IsCompletedSuccessfully, "The real HTTP wave must arrive before import terminates.");
            Assert.AreEqual(concurrency, endpoint.Peak);
        }
        catch
        {
            endpoint.Release();
            await importing;
            throw;
        }
        finally { endpoint.Release(); }

        var result = (await importing).ReadEnvelope(0)["data"]!;
        Assert.IsTrue(result["complete"]!.GetValue<bool>());
        Assert.AreEqual(concurrency, endpoint.Peak);
        Assert.AreEqual(4, endpoint.Requests);
        Assert.AreEqual(4, result["pack"]?["acquisition"]?["downloadedDeclaredFiles"]?.GetValue<int>());
        var records = result["pack"]!["files"]!.AsArray();
        Assert.HasCount(4, records);
        var game = result["instance"]!["gameDirectory"]!.GetValue<string>();
        for (var index = 0; index < 4; index++)
        {
            var name = $"library{index}.jar";
            var payload = InstallationEndpoint.Payload(name);
            Assert.AreEqual("mods/" + name, records[index]?["path"]?.ToString());
            Assert.AreEqual(Convert.ToHexStringLower(SHA512.HashData(payload)), records[index]?["sha512"]?.ToString());
            CollectionAssert.AreEqual(payload, await File.ReadAllBytesAsync(Path.Combine(game, "mods", name), TestContext.CancellationToken));
        }
    }

    private static string CreateArchive(string root, Uri endpoint)
    {
        var files = new JsonArray();
        for (var index = 0; index < 4; index++)
        {
            var name = $"library{index}.jar";
            var payload = InstallationEndpoint.Payload(name);
            files.Add(new JsonObject
            {
                ["path"] = "mods/" + name,
                ["fileSize"] = payload.Length,
                ["hashes"] = new JsonObject
                {
                    ["sha1"] = Convert.ToHexStringLower(SHA1.HashData(payload)),
                    ["sha512"] = Convert.ToHexStringLower(SHA512.HashData(payload))
                },
                ["downloads"] = new JsonArray(new Uri(endpoint, name).AbsoluteUri)
            });
        }

        var path = Path.Combine(root, "concurrent.mrpack");
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        using var writer = new StreamWriter(archive.CreateEntry("modrinth.index.json").Open());
        writer.Write(new JsonObject
        {
            ["formatVersion"] = 1,
            ["game"] = "minecraft",
            ["name"] = "Concurrent fixture",
            ["versionId"] = "one",
            ["dependencies"] = new JsonObject { ["minecraft"] = "1.20.1" },
            ["files"] = files
        }.ToJsonString());
        return path;
    }

    /// <summary>Gets or sets the runner deadline for the held network wave and child process.</summary>
    public TestContext TestContext { get; set; } = null!;
}