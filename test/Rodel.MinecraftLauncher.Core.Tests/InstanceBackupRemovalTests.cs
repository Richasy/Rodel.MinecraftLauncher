using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Backups;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects instance-owned archive cleanup and retry boundaries, separate from archive digest validation.</summary>
[TestClass]
public sealed class InstanceBackupRemovalTests
{
    /// <summary>Missing ownership must explain rejection without deleting game data, registration or archives.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Remove_UnownedFilesRejectsWithoutMutation(bool mismatchedMarker)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instances = new InstanceStore(paths);
            var instance = await instances.CreateAsync("unowned", "synthetic", TestContext.CancellationToken);
            var payload = Path.Combine(instance.GameDirectory, "player-data.txt");
            await File.WriteAllTextAsync(payload, "preserve", TestContext.CancellationToken);
            var backup = await new BackupService(paths).CreateAsync(instance.Id, cancellationToken: TestContext.CancellationToken);
            var archive = Path.Combine(paths.BackupsDirectory, backup.Id + ".zip");
            var archiveBytes = await File.ReadAllBytesAsync(archive, TestContext.CancellationToken);
            // Relinking revokes ownership even when the path and old marker still match, as in the reported failure.
            if (mismatchedMarker)
                await File.WriteAllTextAsync(Path.Combine(instance.GameDirectory, ".rodel-instance-owner"), "another-instance", TestContext.CancellationToken);
            else
                instance = await instances.RelinkAsync(instance.Id, instance.GameDirectory, instance.MinecraftDirectory, TestContext.CancellationToken);
            var metadata = await File.ReadAllTextAsync(instance.MetadataPath, TestContext.CancellationToken);

            var error = await Assert.ThrowsAsync<LauncherOperationException>(() => instances.RemoveAsync(instance.Id, true, TestContext.CancellationToken));

            Assert.AreEqual("INSTANCE_FILES_NOT_OWNED", error.Code);
            Assert.AreEqual(metadata, await File.ReadAllTextAsync(instance.MetadataPath, TestContext.CancellationToken));
            Assert.AreEqual("preserve", await File.ReadAllTextAsync(payload, TestContext.CancellationToken));
            CollectionAssert.AreEqual(archiveBytes, await File.ReadAllBytesAsync(archive, TestContext.CancellationToken));
            Assert.IsFalse(File.Exists(Path.Combine(Path.GetDirectoryName(instance.MetadataPath)!, "removal.json")));
        });
    }

    /// <summary>Both removal modes delete owned archives by manifest ownership and actual path, preserving other or unidentifiable data.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Remove_DeletesOwnedBackupsOnly(bool deleteFiles)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instances = new InstanceStore(paths);
            var first = await instances.CreateAsync("first", "synthetic", TestContext.CancellationToken);
            var second = await instances.CreateAsync("second", "synthetic", TestContext.CancellationToken);
            var backups = new BackupService(paths);
            var owned = await backups.CreateAsync(first.Id, cancellationToken: TestContext.CancellationToken);
            var other = await backups.CreateAsync(second.Id, cancellationToken: TestContext.CancellationToken);
            var ownedPath = Path.Combine(paths.BackupsDirectory, "renamed.zip");
            File.Move(Path.Combine(paths.BackupsDirectory, owned.Id + ".zip"), ownedPath);
            // A manifest ID is not a deletion path. Redirecting it must never delete the other instance's archive.
            using (var archive = ZipFile.Open(ownedPath, ZipArchiveMode.Update))
            {
                var entry = archive.GetEntry("manifest.json")!;
                JsonObject manifest;
                using (var input = entry.Open()) manifest = JsonNode.Parse(input)!.AsObject();
                manifest["id"] = other.Id;
                entry.Delete();
                using var output = new StreamWriter(archive.CreateEntry("manifest.json").Open());
                output.Write(manifest.ToJsonString());
            }

            var unknownPath = Path.Combine(paths.BackupsDirectory, "unknown.zip");
            await File.WriteAllTextAsync(unknownPath, "unidentifiable", TestContext.CancellationToken);
            await instances.RemoveAsync(first.Id, deleteFiles, TestContext.CancellationToken);

            Assert.IsFalse(File.Exists(first.MetadataPath));
            Assert.IsFalse(File.Exists(ownedPath));
            Assert.AreEqual(!deleteFiles, Directory.Exists(first.GameDirectory));
            Assert.AreEqual(second.Id, backups.Inspect(other.Id!).InstanceId);
            Assert.AreEqual("unidentifiable", await File.ReadAllTextAsync(unknownPath, TestContext.CancellationToken));
        });
    }

    /// <summary>A real archive delete failure must retain registration and allow retry even after owned game files are gone.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Remove_LockedArchiveRetainsRegistrationForRetry(bool deleteFiles)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instances = new InstanceStore(paths);
            var instance = await instances.CreateAsync("retry", "synthetic", TestContext.CancellationToken);
            var backups = new BackupService(paths);
            var backup = await backups.CreateAsync(instance.Id, cancellationToken: TestContext.CancellationToken);
            var archivePath = Path.Combine(paths.BackupsDirectory, backup.Id + ".zip");
            // Allow ownership inspection while denying deletion at the actual destructive boundary.
            using (var blocker = new FileStream(archivePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                await Assert.ThrowsAsync<IOException>(() => instances.RemoveAsync(instance.Id, deleteFiles, TestContext.CancellationToken));
                Assert.IsTrue(File.Exists(instance.MetadataPath));
                Assert.IsTrue(File.Exists(archivePath));
            }

            await instances.RemoveAsync(instance.Id, deleteFiles, TestContext.CancellationToken);
            Assert.IsFalse(File.Exists(instance.MetadataPath));
            Assert.IsFalse(File.Exists(archivePath));
            Assert.AreEqual(!deleteFiles, Directory.Exists(instance.GameDirectory));
        });
    }

    /// <summary>Queued create/restore must re-read registration after acquiring its lease, rejecting a deleted instance instead of reviving files.</summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task Backup_WaitingOnDeletedRegistration_DoesNotUseStaleInstance(bool restore)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var instance = await new InstanceStore(paths).CreateAsync("queued", "synthetic", TestContext.CancellationToken);
            var backups = new BackupService(paths);
            var backup = await backups.CreateAsync(instance.Id, cancellationToken: TestContext.CancellationToken);
            Task pending;
            // The held real leases deterministically suspend both the old game-only path and the metadata-aware path.
            // Removing metadata models the removal commit before the queued operation can acquire ownership.
            using (await ResourceLease.AcquireAsync(instance.MetadataPath, TestContext.CancellationToken))
            using (await ResourceLease.AcquireAsync(instance.GameDirectory, TestContext.CancellationToken))
            {
                pending = restore
                    ? backups.RestoreAsync(backup.Id!, cancellationToken: TestContext.CancellationToken)
                    : backups.CreateAsync(instance.Id, cancellationToken: TestContext.CancellationToken);
                Assert.IsFalse(pending.IsCompleted);
                File.Delete(instance.MetadataPath);
            }

            await Assert.ThrowsAsync<FileNotFoundException>(async () => await pending);
            Assert.HasCount(1, Directory.GetFiles(paths.BackupsDirectory, "*.zip"));
            Assert.IsEmpty(Directory.GetFiles(paths.BackupsDirectory, "*.restore.json"));
        });
    }

    /// <summary>Gets or sets cancellation for isolated archive and instance operations.</summary>
    public TestContext TestContext { get; set; }
}