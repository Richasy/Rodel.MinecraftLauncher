// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.GameBridge;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects installed Forge mapping lookup across shared and unmarked hand-configured runtime layouts.</summary>
[TestClass]
public sealed class GameBridgeMappingTests
{
    /// <summary>
    /// Review: looking only beneath runtime/libraries rejects a valid managed Forge instance before its bridge starts.
    /// Real ZIP files and independently declared TSRG overloads exercise lookup/parsing; no network or Minecraft is substituted as a claimed game run.
    /// Existing bridge transport/JVM tests do not cross shared dependency routing.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public Task InstalledMcpConfiguration_UsesItsRuntimeLayout_AndRetainsExactOverloads(bool shared)
        => ReportStorage.RunAsync(async paths =>
        {
            var ct = TestContext.CancellationToken;
            var runtime = Path.Combine(paths.InstancesDirectory, "mapping-scope", "runtime");
            Directory.CreateDirectory(runtime);
            var set = new string('a', 64);
            var libraries = shared ? Path.Combine(paths.DependenciesDirectory, "libraries", set) : Path.Combine(runtime, "libraries");
            if (shared) await JsonFiles.WriteAsync(Path.Combine(runtime, SharedRuntimeLayout.FileName), new JsonObject { ["schemaVersion"] = 1, ["libraries"] = set }, ct);
            var file = Path.Combine(libraries, "de", "oceanlabs", "mcp", "mcp_config", "1.20.1-20230612.114412", "mcp_config-1.20.1-20230612.114412.zip");
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            const string mapping = "tsrg2 obf srg id\nabc net/minecraft/Example 0\n\ta f_1_ 0\n\tb (I)V m_2_ 0\n\t\t0 ignored 0\n\tb (J)V m_3_ 0\n";
            using (var archive = ZipFile.Open(file, ZipArchiveMode.Create))
            {
                using var writer = new StreamWriter(archive.CreateEntry("config/joined.tsrg").Open());
                await writer.WriteAsync(mapping.AsMemory(), ct);
            }

            var metadata = new JsonObject { ["_minecraftVersion"] = "1.20.1", ["_rodelMcpConfig"] = "de.oceanlabs.mcp:mcp_config:1.20.1-20230612.114412@zip" };
            var actual = await BridgeSrgMappings.ReadAsync(runtime, metadata, ct);
            Assert.AreEqual(mapping, actual);
            var members = BridgeSrgMappings.Parse(actual);
            Assert.HasCount(3, members);
            Assert.AreEqual("f_1_", members[("abc", "a", string.Empty)]);
            Assert.AreEqual("m_2_", members[("abc", "b", "(I)V")]);
            Assert.AreEqual("m_3_", members[("abc", "b", "(J)V")]);
            // Review: an offline Forge launch must reuse the installed raw Mojmap, while an equally sized corrupt
            // file must remain unavailable. ZIP/SRG lookup alone cannot detect that separate preparation failure.
            var raw = Encoding.UTF8.GetBytes("net.minecraft.client.Minecraft -> abc:\n    void tick() -> b\n");
            var rawFile = Path.Combine(libraries, "net", "minecraft", "client", "1.20.1-20230612.114412", "client-1.20.1-20230612.114412-mappings.txt");
            Directory.CreateDirectory(Path.GetDirectoryName(rawFile)!);
            await File.WriteAllBytesAsync(rawFile, raw, ct);
            var sha1 = Convert.ToHexStringLower(SHA1.HashData(raw));
            CollectionAssert.AreEqual(raw, await BridgeSrgMappings.ReadOfficialClientMappingsAsync(runtime, metadata, sha1, raw.Length, ct));
            raw[0] ^= 1;
            await File.WriteAllBytesAsync(rawFile, raw, ct);
            Assert.IsNull(await BridgeSrgMappings.ReadOfficialClientMappingsAsync(runtime, metadata, sha1, raw.Length, ct));
        });

    /// <summary>Gets or sets cooperative cancellation supplied by the test runner.</summary>
    public TestContext TestContext { get; set; }
}