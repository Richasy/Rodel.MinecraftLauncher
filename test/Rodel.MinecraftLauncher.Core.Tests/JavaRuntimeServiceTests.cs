using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Diagnostics;
using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects runtime ownership, permanent removal, portable registration and catalog fallback, not actual JDK downloads or gameplay.</summary>
[TestClass]
[TestCategory("Integration")]
public sealed class JavaRuntimeServiceTests
{
    /// <summary>Removing an owned runtime must release its bytes, not move them into an accumulating recovery folder; both storage layouts remain supported.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Remove_ManagedArchive_PermanentlyDeletesOwnedFiles(bool legacyLayout)
    {
        using var fixture = new JavaProbeFixture();
        var archive = fixture.CreateArchive();
        var original = File.ReadAllBytes(archive);
        var service = new JavaRuntimeService(fixture.Paths);
        await service.ImportAsync(archive, "managed", TestContext.CancellationToken);
        var directory = Path.Combine(fixture.Paths.RuntimesDirectory, "managed");
        if (legacyLayout)
        {
            var legacy = Path.Combine(fixture.Root, "runtimes", "managed");
            Directory.CreateDirectory(Path.GetDirectoryName(legacy)!);
            Directory.Move(directory, legacy);
            directory = legacy;
        }

        var result = await service.RemoveAsync("managed", TestContext.CancellationToken);
        Assert.IsTrue(result.Removed);
        Assert.IsFalse(Directory.Exists(directory));
        Assert.IsFalse(JsonSerializer.SerializeToNode(result, JavaOperationJsonContext.Default.JavaRemovalResult)!.AsObject().ContainsKey("recoveryDirectory"));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.RuntimesDirectory, ".removed")));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(archive), "The source archive is not owned runtime storage.");
    }

    /// <summary>An association owns its registration, never the selected external bytes, even when the selected files are inside the registration directory.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Remove_ExternalAssociation_PreservesOriginalFiles(bool insideRegistration)
    {
        using var fixture = new JavaProbeFixture();
        var directory = Path.Combine(fixture.Paths.RuntimesDirectory, "external");
        var source = fixture.Home;
        if (insideRegistration)
        {
            source = Path.Combine(directory, "existing");
            Directory.CreateDirectory(directory);
            Directory.Move(fixture.Home, source);
        }

        var executable = Path.Combine(source, "bin", "java.exe");
        var original = File.ReadAllBytes(executable);
        var service = new JavaRuntimeService(fixture.Paths);
        await service.ImportAsync(source, "external", TestContext.CancellationToken);
        var result = await service.RemoveAsync("external", TestContext.CancellationToken);
        Assert.IsFalse(result.Managed);
        Assert.IsFalse(File.Exists(Path.Combine(directory, "registration.json")));
        CollectionAssert.AreEqual(original, File.ReadAllBytes(executable));
        if (insideRegistration)
            Assert.IsNotEmpty(result.Warnings, "Retained external files must be explained, not recursively deleted.");
        else
            Assert.IsFalse(Directory.Exists(directory));
    }

    /// <summary>Configuration and a live process identity must reject deletion before any runtime bytes or registration are removed.</summary>
    [TestMethod]
    [DataRow("global-id")]
    [DataRow("global-path")]
    [DataRow("instance")]
    [DataRow("session")]
    public async Task Remove_ReferencedRuntime_RejectsWithoutDeletingFiles(string reference)
    {
        using var fixture = new JavaProbeFixture();
        var service = new JavaRuntimeService(fixture.Paths);
        var runtime = await service.ImportAsync(fixture.CreateArchive(), "managed", TestContext.CancellationToken);
        var executable = runtime.JavaPath;
        var settings = new SettingsService(fixture.Paths);
        switch (reference)
        {
            case "global-id":
                await settings.SetAsync("javaRuntimeId", JsonValue.Create("managed"), cancellationToken: TestContext.CancellationToken);
                break;
            case "global-path":
                await settings.SetAsync("javaPath", JsonValue.Create(executable), cancellationToken: TestContext.CancellationToken);
                break;
            case "instance":
                var instance = await new InstanceStore(fixture.Paths).CreateAsync("Synthetic instance", "1.21.1", TestContext.CancellationToken);
                await settings.SetAsync("javaRuntimeId", JsonValue.Create("managed"), instance, TestContext.CancellationToken);
                break;
            case "session":
                // The real test-host identity exercises activity recognition; no JVM or Minecraft is claimed.
                using (var process = Process.GetCurrentProcess())
                {
                    await JsonFiles.WriteAsync(Path.Combine(fixture.Paths.SessionsDirectory, Guid.NewGuid().ToString("N") + ".json"),
                        new JsonObject
                        {
                            ["status"] = "running",
                            ["processId"] = process.Id,
                            ["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O"),
                            ["javaPath"] = executable
                        },
                        TestContext.CancellationToken);
                }

                break;
        }

        await Assert.ThrowsAsync<IOException>(() => service.RemoveAsync("managed", TestContext.CancellationToken));
        Assert.IsTrue(File.Exists(executable));
        Assert.IsTrue(File.Exists(Path.Combine(fixture.Paths.RuntimesDirectory, "managed", "registration.json")));
    }

    /// <summary>A sharing failure may partially delete owned files, but must retain registration so closing the handle permits a real retry.</summary>
    [TestMethod]
    public async Task Remove_LockedManagedExecutable_KeepsRegistrationUntilRetrySucceeds()
    {
        using var fixture = new JavaProbeFixture();
        var service = new JavaRuntimeService(fixture.Paths);
        var runtime = await service.ImportAsync(fixture.CreateArchive(), "managed", TestContext.CancellationToken);
        var directory = Path.Combine(fixture.Paths.RuntimesDirectory, "managed");
        using (var held = new FileStream(runtime.JavaPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            await Assert.ThrowsAsync<IOException>(() => service.RemoveAsync("managed", TestContext.CancellationToken));
        Assert.IsTrue(File.Exists(Path.Combine(directory, "registration.json")));
        await service.RemoveAsync("managed", TestContext.CancellationToken);
        Assert.IsFalse(Directory.Exists(directory));
    }

    /// <summary>A registration-directory junction does not confer ownership of its target; permanent removal must not traverse it.</summary>
    [TestMethod]
    public async Task Remove_RedirectedRegistration_RejectsWithoutTouchingTarget()
    {
        using var fixture = new JavaProbeFixture();
        var service = new JavaRuntimeService(fixture.Paths);
        await service.ImportAsync(fixture.CreateArchive(), "managed", TestContext.CancellationToken);
        var directory = Path.Combine(fixture.Paths.RuntimesDirectory, "managed");
        var target = Path.Combine(fixture.Root, "external-registration");
        Directory.Move(directory, target);
        await JavaProbeFixture.CreateJunctionAsync(directory, target, TestContext.CancellationToken);
        try
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => service.RemoveAsync("managed", TestContext.CancellationToken));
            Assert.IsTrue(File.Exists(Path.Combine(target, "runtime", "bin", "java.exe")));
            Assert.IsTrue(File.Exists(Path.Combine(target, "registration.json")));
        }
        finally { Directory.Delete(directory); }
    }

    /// <summary>The typed request used by App must persist the same permanent-removal outcome without requiring a recovery-directory field.</summary>
    [TestMethod]
    public async Task Task_RemoveJava_PersistsPermanentRemovalResultForApp()
    {
        using var fixture = new JavaProbeFixture();
        var client = new LauncherClient(fixture.Paths);
        await client.JavaRuntimes.ImportAsync(fixture.CreateArchive(), "managed", TestContext.CancellationToken);
        var queued = await client.Tasks.QueueAsync(new RemoveJavaTask("managed"), cancellationToken: TestContext.CancellationToken);
        var id = queued.Id!;
        var result = await client.Tasks.RunAsync(id, TestContext.CancellationToken);
        var finished = client.Jobs.Get(id);
        Assert.AreEqual("succeeded", finished.Status);
        Assert.IsTrue(result!["removed"]!.GetValue<bool>());
        Assert.IsFalse(finished.Result!.AsObject().ContainsKey("recoveryDirectory"));
        Assert.IsFalse(Directory.Exists(Path.Combine(fixture.Paths.RuntimesDirectory, "managed")));
    }

    /// <summary>Moving a data root must not leave an imported managed runtime bound to its original absolute executable path.</summary>
    [TestMethod]
    public async Task Import_ArchiveThenMoveDataRoot_ResolvesAndProbesRelocatedJava()
    {
        using var fixture = new JavaProbeFixture();
        var root = Path.Combine(fixture.Root, "data");
        var service = new JavaRuntimeService(new LauncherPaths(root));
        await service.ImportAsync(fixture.CreateArchive(), "managed", TestContext.CancellationToken);
        var moved = Path.Combine(fixture.Root, "relocated");
        Directory.Move(root, moved);
        var relocated = new JavaRuntimeService(new LauncherPaths(moved));
        var entry = (await relocated.ListAsync(TestContext.CancellationToken)).Single();
        var expected = Path.Combine(moved, "dependencies", "java", "managed", "runtime", "bin", "java.exe");
        Assert.AreEqual(expected, relocated.ResolveJavaPath("managed"));
        Assert.AreEqual(expected, entry.JavaPath);
        Assert.IsTrue(entry.IsUsable);
        Assert.AreEqual("25.0.1", entry.FullVersion);
    }

    /// <summary>Lists the same identity that resolution uses: current storage wins, and a mismatched neighboring record cannot masquerade as it.</summary>
    [TestMethod]
    public async Task List_DuplicateAndInvalidRegistrations_PreservesDirectoryIdentity()
    {
        using var current = new JavaProbeFixture();
        using var legacy = new JavaProbeFixture();
        legacy.SetVersion("26.0.1");
        var service = new JavaRuntimeService(current.Paths);
        await service.ImportAsync(current.Home, "shared", TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(current.Root, "runtimes", "shared", "registration.json"),
            new JsonObject { ["id"] = "shared", ["javaPath"] = legacy.Executable, ["managed"] = false }, TestContext.CancellationToken);
        await JsonFiles.WriteAsync(Path.Combine(current.Paths.RuntimesDirectory, "bad", "registration.json"),
            new JsonObject { ["id"] = "shared", ["javaPath"] = legacy.Executable, ["managed"] = false }, TestContext.CancellationToken);
        var entries = await service.ListAsync(TestContext.CancellationToken);
        Assert.HasCount(2, entries);
        var selected = entries.Single(entry => entry.Id == "shared");
        Assert.AreEqual(current.Executable, selected.JavaPath);
        Assert.AreEqual("25.0.1", selected.FullVersion);
        var invalid = entries.Single(entry => entry.Id == "bad");
        Assert.IsFalse(invalid.IsUsable);
        Assert.IsFalse(string.IsNullOrWhiteSpace(invalid.Warning));
        Assert.AreEqual(0, legacy.Calls, "Neither a shadowed nor a mismatched record should probe another Java.");
    }

    /// <summary>A cache write denial must preserve fresh network candidates instead of silently returning an older catalog.</summary>
    [TestMethod]
    public async Task Catalog_CacheWriteDenied_ReturnsFreshReleasesWithWarning()
    {
        using var fixture = new JavaProbeFixture();
        var cache = Path.Combine(fixture.Paths.CacheDirectory, "java-runtime-all.json");
        await JsonFiles.WriteAsync(cache, Catalog("25.0.1"), TestContext.CancellationToken);
        using var handler = new JavaCatalogHandler(HttpStatusCode.OK, Catalog("25.0.2").ToJsonString());
        using var http = new HttpClient(handler);
        var service = new JavaRuntimeService(fixture.Paths, http, new Uri("https://java.test/catalog.json"));
        File.SetAttributes(cache, FileAttributes.ReadOnly);
        try
        {
            var result = await service.GetAvailableAsync(ct: TestContext.CancellationToken);
            Assert.AreEqual("25.0.2", CatalogVersion(result));
            Assert.AreEqual("network", result.Source);
            Assert.IsFalse(string.IsNullOrWhiteSpace(result.Warning));
            Assert.AreEqual("25.0.1", CatalogVersion(JsonFiles.ReadObject(cache)));
        }
        finally { File.SetAttributes(cache, FileAttributes.Normal); }
    }

    /// <summary>Network failure exposes a usable but identified cached catalog, while an explicit offline read makes no further HTTP request.</summary>
    [TestMethod]
    public async Task Catalog_RequestFails_ReportsCachedFallbackAndHonorsOfflineRead()
    {
        using var fixture = new JavaProbeFixture();
        await JsonFiles.WriteAsync(Path.Combine(fixture.Paths.CacheDirectory, "java-runtime-all.json"), Catalog("25.0.1"), TestContext.CancellationToken);
        using var handler = new JavaCatalogHandler(HttpStatusCode.ServiceUnavailable, string.Empty);
        using var http = new HttpClient(handler);
        var service = new JavaRuntimeService(fixture.Paths, http, new Uri("https://java.test/catalog.json"));
        var fallback = await service.GetAvailableAsync(ct: TestContext.CancellationToken);
        Assert.AreEqual("25.0.1", CatalogVersion(fallback));
        Assert.AreEqual("cache-fallback", fallback.Source);
        Assert.IsFalse(string.IsNullOrWhiteSpace(fallback.Warning));
        var requests = handler.Requests;
        var offline = await service.GetAvailableAsync(offline: true, ct: TestContext.CancellationToken);
        Assert.AreEqual("25.0.1", CatalogVersion(offline));
        Assert.IsTrue(offline.Offline);
        Assert.AreEqual(requests, handler.Requests);
    }

    private static JsonObject Catalog(string version) => new()
    {
        ["windows-x64"] = new JsonObject
        {
            ["java-runtime-epsilon"] = new JsonArray(new JsonObject { ["version"] = new JsonObject { ["name"] = version } })
        }
    };

    private static string CatalogVersion(JsonObject catalog) => catalog["windows-x64"]!["java-runtime-epsilon"]![0]!["version"]!["name"]!.GetValue<string>();

    private static string CatalogVersion(JavaRuntimeCatalog catalog) =>
        catalog.GetPlatform("windows-x64")!["java-runtime-epsilon"]![0]!["version"]!["name"]!.GetValue<string>();

    /// <summary>Gets or sets the runner cancellation context for runtime operations.</summary>
    public TestContext TestContext { get; set; }
}