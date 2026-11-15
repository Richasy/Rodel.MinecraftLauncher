using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Content;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects concurrent typed online tasks at the real HTTP, cancellation and content-transaction boundaries.</summary>
[TestClass]
public sealed class ConcurrentContentDownloadTests
{
    /// <summary>Gets or sets the test runner's cancellation and diagnostic context.</summary>
    public TestContext TestContext { get; set; } = null!;

    /// <summary>
    /// Review: existing plan cases apply one transaction; they cannot detect the old instance-wide task rejection
    /// or lost updates between two downloads. Both real response bodies are held open before either completes;
    /// B commits first, then A must merge an identical dependency or reject a different exact release without overwriting B.
    /// The oracle is installed bytes and durable job/lock results, not merely two started Tasks.
    /// </summary>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public async Task ConcurrentReviews_OverlapAndProtectSharedDependency(bool conflict) => await WithClientAsync(async client =>
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var server = new ContentDownloadServer();
        var instance = await client.Instances.CreateAsync("parallel", "synthetic", ct);
        var commonB = Node(server, "common", "v1", blocked: false);
        var commonA = conflict ? Node(server, "common", "v2", blocked: false) : commonB;
        var a = Node(server, "a", "v1");
        var b = Node(server, "b", "v1");
        var reviewA = await SaveReviewAsync(client, instance, [a.Node, commonA.Node], ct);
        var reviewB = await SaveReviewAsync(client, instance, [b.Node, commonB.Node], ct);
        var jobA = await client.Tasks.StartAsync(new ApplyContentReviewTask(instance.Id, reviewA), ct);
        var jobB = await client.Tasks.StartAsync(new ApplyContentReviewTask(instance.Id, reviewB), ct);
        await Task.WhenAll(a.Transfer.Entered.Task, b.Transfer.Entered.Task).WaitAsync(ct);
        Assert.AreEqual("running", client.Jobs.Get(jobA.Id!).Status);
        Assert.AreEqual("running", client.Jobs.Get(jobB.Id!).Status);

        b.Transfer.Release.SetResult();
        Assert.AreEqual("succeeded", (await FinishedAsync(client, jobB.Id!, ct)).Status);
        Assert.AreEqual("running", client.Jobs.Get(jobA.Id!).Status, "B must finish while A's HTTP body remains blocked.");
        a.Transfer.Release.SetResult();
        var resultA = await FinishedAsync(client, jobA.Id!, ct);
        Assert.AreEqual(conflict ? "failed" : "succeeded", resultA.Status);
        if (conflict) Assert.AreEqual("CONTENT_REVIEW_STALE", resultA.Failure?["error"]?["code"]?.ToString());
        var entries = await client.ListContentAsync(instance.Id, cancellationToken: ct);
        Assert.HasCount(conflict ? 2 : 3, entries);
        CollectionAssert.AreEqual(commonB.Transfer.Bytes, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "mods", commonB.Node.FileName), ct));
        var records = (await client.Content.GetLockAsync(instance.GameDirectory, ct))["files"]!.AsArray();
        Assert.ContainsSingle(record => record?["projectId"]?.ToString() == "common", records);
    });

    /// <summary>
    /// Review: cancellation after an actual partial body must cancel only that receipt and never install its partial file.
    /// A second body remains independently blocked, then commits normally. This reaches cancellation inside DownloadService
    /// and checks real task terminal states/files; existing selection tests only cancel metadata reads.
    /// The remaining mutation blocker must identify B's download, not a game, and disappear only after B actually finishes.
    /// </summary>
    [TestMethod]
    public async Task CancelOnePartialDownload_OtherTaskStillCommits() => await WithClientAsync(async client =>
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var server = new ContentDownloadServer();
        var instance = await client.Instances.CreateAsync("cancel-one", "synthetic", ct);
        var a = Node(server, "a", "v1");
        var b = Node(server, "b", "v1");
        var jobA = await client.Tasks.StartAsync(new ApplyContentReviewTask(instance.Id, await SaveReviewAsync(client, instance, [a.Node], ct)), ct);
        var jobB = await client.Tasks.StartAsync(new ApplyContentReviewTask(instance.Id, await SaveReviewAsync(client, instance, [b.Node], ct)), ct);
        await Task.WhenAll(a.Transfer.Entered.Task, b.Transfer.Entered.Task).WaitAsync(ct);
        await client.Jobs.CancelAsync(jobA.Id!, ct);
        Assert.AreEqual("canceled", (await FinishedAsync(client, jobA.Id!, ct)).Status);
        Assert.AreEqual("running", client.Jobs.Get(jobB.Id!).Status);
        Assert.IsFalse(client.Sessions.HasActiveGame(instance.GameDirectory));
        var blocked = Assert.Throws<LauncherOperationException>(() => client.EnsureInstanceIdle(instance.Id));
        Assert.AreEqual("INSTANCE_BUSY", blocked.Code);
        var activity = blocked.Details!.Deserialize(ApplicationJsonContext.Default.InstanceActivityConflict)!;
        Assert.AreEqual("content-download", activity.Reason);
        Assert.AreEqual(jobB.Id, activity.JobId);
        b.Transfer.Release.SetResult();
        Assert.AreEqual("succeeded", (await FinishedAsync(client, jobB.Id!, ct)).Status);
        client.EnsureInstanceIdle(instance.Id);
        var entries = await client.ListContentAsync(instance.Id, cancellationToken: ct);
        Assert.AreEqual("b-v1.jar", entries.Single().FileName);
        CollectionAssert.AreEqual(b.Transfer.Bytes, await File.ReadAllBytesAsync(Path.Combine(instance.GameDirectory, "mods", "b-v1.jar"), ct));
    });

    /// <summary>
    /// Review: permitting independent additions must not relax protection for a previously reviewed player file.
    /// Editing that file while an actual HTTP body is held open reaches the commit-time drift window; the job must
    /// fail and retain the new player bytes. This is distinct from rejecting two shared remote release choices.
    /// </summary>
    [TestMethod]
    public async Task ExistingFileChangedDuringDownload_CommitPreservesPlayerEdit() => await WithClientAsync(async client =>
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.CancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        var ct = deadline.Token;
        await using var server = new ContentDownloadServer();
        var instance = await client.Instances.CreateAsync("edited-content", "synthetic", ct);
        var retained = Node(server, "retained", "v1", blocked: false);
        var localFile = Path.Combine(client.Paths.Root, "retained.jar");
        await File.WriteAllBytesAsync(localFile, retained.Transfer.Bytes, ct);
        await client.Content.AddAsync(instance.GameDirectory, "mods", localFile, ct: ct);
        var added = Node(server, "added", "v1");
        var review = await SaveReviewAsync(client, instance, [added.Node], ct);
        var job = await client.Tasks.StartAsync(new ApplyContentReviewTask(instance.Id, review), ct);
        await added.Transfer.Entered.Task.WaitAsync(ct);
        var editedBytes = Node(server, "retained", "player-edit", blocked: false).Transfer.Bytes;
        var installed = Path.Combine(instance.GameDirectory, "mods", "retained.jar");
        await File.WriteAllBytesAsync(installed, editedBytes, ct);
        added.Transfer.Release.SetResult();
        var result = await FinishedAsync(client, job.Id!, ct);
        Assert.AreEqual("failed", result.Status);
        Assert.AreEqual("CONTENT_REVIEW_STALE", result.Failure?["error"]?["code"]?.ToString());
        CollectionAssert.AreEqual(editedBytes, await File.ReadAllBytesAsync(installed, ct));
        Assert.IsFalse(File.Exists(Path.Combine(instance.GameDirectory, "mods", added.Node.FileName)));
    });

    private static Task WithClientAsync(Func<LauncherClient, Task> action) => ReportStorage.RunAsync(async paths =>
    {
        var client = new LauncherClient(paths);
        try { await action(client); }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            foreach (var job in client.Jobs.List().Where(job => job.Status is "queued" or "running"))
            {
                await client.Jobs.CancelAsync(job.Id!, cleanup.Token);
                await FinishedAsync(client, job.Id!, cleanup.Token);
            }
        }
    });

    private static async Task<JobStatus> FinishedAsync(LauncherClient client, string id, CancellationToken ct)
    {
        while (true)
        {
            var job = client.Jobs.Get(id);
            if (job.Status is "succeeded" or "failed" or "canceled" or "interrupted") return job;
            await Task.Delay(30, ct);
        }
    }

    private static (ContentPlanNode Node, ContentDownloadServer.Transfer Transfer) Node(ContentDownloadServer server, string project, string version, bool blocked = true)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        using (var writer = new StreamWriter(zip.CreateEntry("fabric.mod.json").Open()))
            writer.Write(new JsonObject { ["schemaVersion"] = 1, ["id"] = project, ["version"] = version, ["name"] = project }.ToJsonString());
        var bytes = output.ToArray();
        var name = project + "-" + version + ".jar";
        return (new()
        {
            Platform = "modrinth",
            ProjectId = project,
            VersionId = version,
            FileName = name,
            Kind = "mods",
            DownloadUrl = new Uri(server.Endpoint, name).AbsoluteUri,
            Size = bytes.Length,
            Sha1 = Convert.ToHexStringLower(SHA1.HashData(bytes)),
            Upstream = new JsonObject { ["dependencies"] = new JsonArray() },
        }, server.Add(name, bytes, blocked));
    }

    private static async Task<string> SaveReviewAsync(LauncherClient client, InstanceRecord instance, IReadOnlyList<ContentPlanNode> nodes, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var review = new ContentReview
        {
            Id = id,
            InstanceId = instance.Id,
            Mode = "online",
            GameDirectory = instance.GameDirectory,
            Context = await client.GetContentContextAsync(instance.Id, ct: ct),
            Fingerprint = Convert.ToHexStringLower(SHA256.HashData([])),
            OnlineBaseline = await client.ListContentAsync(instance.Id, cancellationToken: ct),
            OnlineTracking = await client.Content.GetLockAsync(instance.GameDirectory, ct),
            Plan = new()
            {
                Platform = "modrinth",
                PrimaryProject = nodes[0].ProjectId,
                GameDirectory = instance.GameDirectory,
                LockFingerprint = "missing",
                Nodes = nodes,
                Edges = [],
                Conflicts = [],
                Warnings = [],
                CanApply = true,
            },
        };
        // Provider planning is substituted with exact candidates and real inventory/tracking snapshots.
        // The old global fingerprint deliberately differs: online admission must use those frozen snapshots, not bypass drift checks.
        await JsonFiles.WriteAsync(Path.Combine(client.Paths.Root, "content-reviews", id + ".json"), JsonSerializer.SerializeToNode(review, ContentJsonContext.Default.ContentReview)!, ct);
        return id;
    }
}