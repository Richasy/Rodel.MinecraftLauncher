// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects game activity checks when a UI shares one fresh session observation across instances.</summary>
[TestClass]
public sealed class SessionActivityTests
{
    /// <summary>
    /// Review: batching must preserve queued work and live sidecars of unreadable sessions, without letting
    /// another instance or exited history appear busy. Existing diagnostics cases do not check this guard.
    /// Real isolated records and this process's identity distinguish activity from mere file presence;
    /// rereading after removal ensures the normal mutation guard never retains the observation batch.
    /// </summary>
    [TestMethod]
    public async Task SharedObservation_PreservesActivityBoundariesAndFreshReads()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var service = new SessionService(paths);
            using var process = Process.GetCurrentProcess();
            var games = new[] { Path.Combine(paths.Root, "queued"), Path.Combine(paths.Root, "exited"), Path.Combine(paths.Root, "unreadable") };
            var files = new List<string>();
            for (var index = 0; index < games.Length; index++)
            {
                var id = Guid.NewGuid().ToString("N");
                var file = Path.Combine(paths.SessionsDirectory, id + ".json");
                files.Add(file);
                var record = new JsonObject
                {
                    ["id"] = id,
                    ["gameDirectory"] = games[index],
                    ["status"] = index == 0 ? "queued" : index == 1 ? "exited" : "running",
                    ["createdAt"] = DateTimeOffset.UtcNow.ToString("O"),
                    ["processId"] = process.Id,
                    ["processStartUtc"] = process.StartTime.ToUniversalTime().ToString("O")
                };
                if (index == 2)
                {
                    await File.WriteAllTextAsync(file, "{broken", TestContext.CancellationToken);
                    await JsonFiles.WriteAsync(file + ".activity", record, TestContext.CancellationToken);
                }
                else
                {
                    await JsonFiles.WriteAsync(file, record, TestContext.CancellationToken);
                }
            }

            var observations = service.List();
            Assert.HasCount(3, observations);
            Assert.IsTrue(SessionService.HasActiveGame(games[0], observations));
            Assert.IsFalse(SessionService.HasActiveGame(games[1], observations));
            Assert.IsTrue(SessionService.HasActiveGame(games[2], observations));
            Assert.IsFalse(SessionService.HasActiveGame(Path.Combine(paths.Root, "unrelated"), observations));

            foreach (var file in files)
                File.Delete(file);
            Assert.IsFalse(service.HasActiveGame(games[0]));
            Assert.IsFalse(service.HasActiveGame(games[2]));
        });
    }

    /// <summary>Supplies the runner cancellation deadline for isolated record writes.</summary>
    public TestContext TestContext { get; set; } = null!;
}