using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects recovery discovery from mistaking an old or unstarted session for completed preparation.</summary>
[TestClass]
public sealed class InstallationRecoveryTests
{
    /// <summary>
    /// An interrupted install remains recoverable until a newer real process used that same profile.
    /// Job lifecycle tests cannot detect stale recovery after the ordinary launch path repairs the files.
    /// Real typed admission and persisted synthetic session evidence exercise discovery without Java or downloads.
    /// The variants distinguish an unstarted session, an older game and a different version from later preparation.
    /// </summary>
    [TestMethod]
    [DataRow("fixture", "2026-01-03T00:00:00Z", false, 1)]
    [DataRow("fixture", "2026-01-01T00:00:00Z", true, 1)]
    [DataRow("other-profile", "2026-01-03T00:00:00Z", true, 1)]
    [DataRow("fixture", "2026-01-03T00:00:00Z", true, 0)]
    public async Task ReadPending_LaterMatchingProcessOnly_ClearsInterruptedInstallation(
        string version, string started, bool hasProcess, int expectedRecoveries)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var client = new LauncherClient(paths);
            var instance = await client.Instances.CreateAsync("recovery-fixture", "fixture", TestContext.CancellationToken);
            var queued = await client.Tasks.QueueAsync(new InstallInstanceTask(instance.Id, new()), cancellationToken: TestContext.CancellationToken);
            var jobPath = Path.Combine(paths.JobsDirectory, queued.Id + ".json");
            var job = JsonFiles.ReadObject(jobPath);
            job["status"] = "canceled";
            job["createdAt"] = "2026-01-02T00:00:00Z";
            await JsonFiles.WriteAsync(jobPath, job, TestContext.CancellationToken);
            var playerFile = Path.Combine(instance.GameDirectory, "player-file.txt");
            await File.WriteAllTextAsync(playerFile, "preserved", TestContext.CancellationToken);
            Assert.HasCount(1, client.InstallationRecovery.ReadPending());

            var id = Guid.NewGuid().ToString("N");
            await JsonFiles.WriteAsync(Path.Combine(paths.SessionsDirectory, id + ".json"), new JsonObject
            {
                ["id"] = id,
                ["instanceId"] = instance.Id,
                ["versionId"] = version,
                ["status"] = hasProcess ? "exited" : "queued",
                ["createdAt"] = started,
                ["processId"] = hasProcess ? 123 : null,
                ["processStartUtc"] = hasProcess ? started : null,
                ["exitCode"] = 0
            }, TestContext.CancellationToken);

            Assert.HasCount(expectedRecoveries, client.InstallationRecovery.ReadPending());
            Assert.AreEqual("preserved", await File.ReadAllTextAsync(playerFile, TestContext.CancellationToken));
            Assert.AreEqual(instance.GameDirectory, client.Instances.Get(instance.Id).GameDirectory);
        });
    }

    /// <summary>Gets or sets the runner cancellation deadline used by the isolated storage fixture.</summary>
    public TestContext TestContext { get; set; } = null!;
}