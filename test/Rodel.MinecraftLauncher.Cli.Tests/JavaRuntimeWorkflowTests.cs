using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Cli.Tests;

/// <summary>Checks permanent runtime removal through actual CLI hosts and its persisted file effects.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaRuntimeWorkflowTests
{
    /// <summary>Listing an imported runtime must preserve registration extensions; removal then deletes only the owned copy.
    /// ZIP and gzip/TAR cross different production readers; the sequential TAR reader must retain its borrowed entry stream across copying.</summary>
    [TestMethod]
    [DataRow("zip")]
    [DataRow("tar.gz")]
    public async Task ImportThenRemove_ManagedJava_DeletesOwnedCopyAndPreservesSource(string archiveKind)
    {
        using var sandbox = new CliSandbox();
        var fixture = new JavaProbeFixture(sandbox.Root);
        var archive = Path.Combine(sandbox.Root, "java." + archiveKind);
        if (archiveKind == "zip")
        {
            ZipFile.CreateFromDirectory(fixture.Home, archive);
        }
        else
        {
            using var output = File.Create(archive);
            using var gzip = new GZipStream(output, CompressionLevel.Fastest);
            TarFile.CreateFromDirectory(fixture.Home, gzip, includeBaseDirectory: false);
        }

        var imported = (await sandbox.RunAsync("java", "import", archive, "--id", "managed")).ReadEnvelope(0);
        var owned = imported["data"]!["runtimeDirectory"]!.GetValue<string>();
        Assert.IsTrue(Directory.Exists(owned));
        var registrationPath = Path.Combine(sandbox.Root, "dependencies", "java", "managed", "registration.json");
        var registration = JsonNode.Parse(File.ReadAllText(registrationPath))!.AsObject();
        registration["providerExtension"] = null;
        File.WriteAllText(registrationPath, registration.ToJsonString());
        var listed = (await sandbox.RunAsync("java", "managed")).ReadEnvelope(0)["data"]!.AsArray().Single()!.AsObject();
        Assert.AreEqual(Path.Combine(owned, "bin", "java.exe"), listed["javaPath"]!.GetValue<string>());
        Assert.IsTrue(listed["isUsable"]!.GetValue<bool>());
        Assert.IsTrue(listed.ContainsKey("providerExtension"));
        Assert.IsNull(listed["providerExtension"]);
        var removed = (await sandbox.RunAsync("java", "remove", "managed")).ReadEnvelope(0);
        Assert.IsTrue(removed["data"]!["removed"]!.GetValue<bool>());
        Assert.IsFalse(removed["data"]!.AsObject().ContainsKey("recoveryDirectory"));
        Assert.IsFalse(Directory.Exists(owned));
        Assert.IsFalse(Directory.Exists(Path.Combine(sandbox.Root, "dependencies", "java", ".removed")));
        Assert.IsTrue(File.Exists(archive));
        Assert.IsTrue(File.Exists(fixture.Executable));
    }
}