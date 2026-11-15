using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Storage;
using Rodel.MinecraftLauncher.Core.Tests.Infrastructure;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Tests;

/// <summary>Protects durable task-envelope and progress observation independently of operation-specific workflows.</summary>
[TestClass]
public sealed class JobReadTests
{
    /// <summary>
    /// Unknown or malformed observation fields must survive real sidecar reads and task completion.
    /// Existing operation tests cover work, not lossless task envelopes. Independent stored JSON catches added null fields,
    /// dropped extensions and request mutation while a real executor callback proves the original parameters still arrive.
    /// The percent variants protect re-export of normal unknown totals and malformed external values without duplicate keys.
    /// </summary>
    [TestMethod]
    [DataRow("null")]
    [DataRow("\"external-value\"")]
    [DataRow("42.5")]
    public async Task Run_ExtensibleRequestAndProgress_PreservesObservedDataAndDispatchesOriginalParameters(string percent)
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var jobs = new JobService(paths);
            var parameters = new JsonObject { ["vendorTask"] = new JsonArray("preserved", null, 17) };
            var created = await jobs.CreateAsync(new("fixture", parameters), ct: TestContext.CancellationToken);
            var file = Path.Combine(paths.JobsDirectory, created.Id + ".json");
            var stored = JsonFiles.ReadObject(file);
            stored["request"]!["vendorEnvelope"] = null;
            var expectedRequest = stored["request"]!.DeepClone();
            await JsonFiles.WriteAsync(file, stored, TestContext.CancellationToken);
            var progress = new JsonObject
            {
                ["stage"] = "copy",
                ["bytes"] = 64,
                ["totalBytes"] = null,
                ["totalFiles"] = "external-value",
                ["overallPercent"] = JsonNode.Parse(percent),
                ["vendorProgress"] = new JsonArray(false, null)
            };
            await JsonFiles.WriteAsync(Path.ChangeExtension(file, ".progress"), progress, TestContext.CancellationToken);

            var observed = jobs.Get(created.Id!);
            Assert.AreEqual(64L, observed.Progress?.Bytes);
            Assert.IsNull(observed.Progress?.TotalFiles);
            Assert.IsTrue(JsonNode.DeepEquals(progress, JsonSerializer.SerializeToNode(observed.Progress, ApplicationJsonContext.Progress)));
            var serialized = JsonSerializer.SerializeToNode(observed, ApplicationJsonContext.Job)!;
            Assert.IsTrue(JsonNode.DeepEquals(expectedRequest, serialized["request"]));
            Assert.IsTrue(JsonNode.DeepEquals(progress, serialized["progress"]));

            var invoked = false;
            await jobs.RunAsync(created.Id!, (definition, _) =>
            {
                invoked = true;
                Assert.AreEqual("fixture", definition.Operation);
                Assert.IsTrue(JsonNode.DeepEquals(parameters, definition.Parameters));
                return Task.FromResult<JsonNode?>(null);
            }, TestContext.CancellationToken);

            Assert.IsTrue(invoked);
            var completed = jobs.Get(created.Id!);
            Assert.AreEqual("succeeded", completed.Status);
            var final = JsonSerializer.SerializeToNode(completed, ApplicationJsonContext.Job)!;
            Assert.IsTrue(JsonNode.DeepEquals(expectedRequest, final["request"]));
            Assert.IsTrue(JsonNode.DeepEquals(progress, final["progress"]));
        });
    }

    /// <summary>
    /// A readable but invalid task request remains inspectable without becoming executable after typed projection.
    /// The persisted array is the independent invalid input; zero callback invocations and a failed stored state reject
    /// a permissive empty-parameters fallback rather than merely checking a DTO getter.
    /// </summary>
    [TestMethod]
    public async Task Run_MalformedParameters_RetainsEvidenceAndNeverInvokesExecutor()
    {
        await ReportStorage.RunAsync(async paths =>
        {
            var jobs = new JobService(paths);
            var created = await jobs.CreateAsync(new("fixture", []), ct: TestContext.CancellationToken);
            var file = Path.Combine(paths.JobsDirectory, created.Id + ".json");
            var stored = JsonFiles.ReadObject(file);
            stored["request"]!["parameters"] = new JsonArray("not-an-object");
            var expectedRequest = stored["request"]!.DeepClone();
            await JsonFiles.WriteAsync(file, stored, TestContext.CancellationToken);

            var observed = jobs.Get(created.Id!);
            Assert.IsTrue(JsonNode.DeepEquals(expectedRequest, JsonSerializer.SerializeToNode(observed, ApplicationJsonContext.Job)!["request"]));
            var invoked = false;
            await Assert.ThrowsExactlyAsync<InvalidDataException>(() => jobs.RunAsync(created.Id!, (_, _) =>
            {
                invoked = true;
                return Task.FromResult<JsonNode?>(null);
            }, TestContext.CancellationToken));

            Assert.IsFalse(invoked);
            var failed = jobs.Get(created.Id!);
            Assert.AreEqual("failed", failed.Status);
            Assert.IsTrue(JsonNode.DeepEquals(expectedRequest, JsonSerializer.SerializeToNode(failed, ApplicationJsonContext.Job)!["request"]));
        });
    }

    /// <summary>Gets or sets cancellation for isolated task storage and execution.</summary>
    public TestContext TestContext { get; set; }
}