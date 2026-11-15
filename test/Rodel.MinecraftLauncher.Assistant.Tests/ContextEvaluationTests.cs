using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting.Storage;
using Rodel.MinecraftLauncher.Core.Application.AI;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Uses MEAI's evaluator/report store on real Core source extraction; inputs are public synthetic Chinese evidence.</summary>
[TestClass]
public sealed class ContextEvaluationTests
{
    /// <summary>
    /// A reusable report must distinguish the real source-grounded checkpoint from controlled negation, identifier and
    /// unknown-effect corruption, then persist/reopen those metrics through the official reporting store.
    /// Existing binary archive assertions do not provide comparable per-candidate evaluation results. These literal
    /// metrics deliberately do not claim semantic model quality and never replace source/hash/permission assertions.
    /// </summary>
    [TestMethod]
    [Timeout(15000, CooperativeCancellation = true)]
    public async Task ChineseEvidenceEvaluation_SeparatesKnownCorruptions_AndPersistsComparableReports()
    {
        string[] evidence =
        [
            "必须先备份，未确认前不要改配置。",
            "编号是 MIGR-582，不是 MIGR-528。",
            "之前的写入效果未知，不能重放。"
        ];
        using var sandbox = new AiSandbox();
        var conversation = await sandbox.ConversationAsync(TestContext.CancellationToken);
        var turn = await sandbox.TurnAsync(conversation, false, TestContext.CancellationToken);
        var history = new JsonArray(evidence.Select(text => (JsonNode)new JsonObject { ["role"] = "user", ["text"] = text }).ToArray());
        var archives = new AiContextArchives(sandbox.Paths);
        var archive = await archives.SaveAsync(turn, history, TestContext.CancellationToken);
        var sources = new AiContextExtraction(archives, conversation["id"]!.ToString()).Sources(archive, [0, 1, 2]);
        var original = AiContextExtraction.Render(sources, sources.Count);
        var candidates = new Dictionary<string, string>
        {
            ["source-grounded"] = original,
            ["negation-lost"] = original.Replace("不要改配置", "可以改配置", StringComparison.Ordinal),
            ["identifier-changed"] = original.Replace("MIGR-582", "MIGR-852", StringComparison.Ordinal),
            ["unknown-effect-invented-complete"] = original.Replace("效果未知", "已经完成", StringComparison.Ordinal)
        };
        var root = Path.Combine(AppContext.BaseDirectory, "TestResults", "meai-evaluation", Guid.NewGuid().ToString("N"));
        var reporting = DiskBasedReportingConfiguration.Create(root, [new ExactEvidenceEvaluator(evidence)],
            enableResponseCaching: false, executionName: "synthetic-chinese-source-fidelity");
        ChatMessage[] request = [new(ChatRole.User, "只保留原始证据，不改写授权、编号、否定或未知效果。\n" + string.Join("\n", evidence))];
        foreach (var candidate in candidates)
        {
            await using var run = await reporting.CreateScenarioRunAsync("checkpoint-evidence", candidate.Key,
                cancellationToken: TestContext.CancellationToken);
            var evaluation = await run.EvaluateAsync(request, new ChatResponse(new ChatMessage(ChatRole.Assistant, candidate.Value)),
                cancellationToken: TestContext.CancellationToken);
            var metric = (NumericMetric)evaluation.Metrics[ExactEvidenceEvaluator.MetricName];
            Assert.IsNotNull(metric.Interpretation);
            Assert.AreEqual(candidate.Key != "source-grounded", metric.Interpretation.Failed);
            if (candidate.Key == "source-grounded") Assert.AreEqual(100d, metric.Value);
            else Assert.IsTrue(metric.Value is >= 0 and < 100);
        }

        var reopened = new Dictionary<string, NumericMetric>();
        await foreach (var result in reporting.ResultStore.ReadResultsAsync(cancellationToken: TestContext.CancellationToken))
            reopened.Add(result.IterationName, (NumericMetric)result.EvaluationResult.Metrics[ExactEvidenceEvaluator.MetricName]);
        Assert.HasCount(candidates.Count, reopened);
        Assert.IsFalse(reopened["source-grounded"].Interpretation!.Failed);
        Assert.IsTrue(reopened["negation-lost"].Interpretation!.Failed);
        Assert.AreEqual(archive.ContentHash, archives.Load(conversation["id"]!.ToString(), archive.Id).ContentHash);
        TestContext.WriteLine("Synthetic MEAI evaluation reports: " + root);
        foreach (var file in Directory.GetFiles(root, "*.json", SearchOption.AllDirectories)) TestContext.AddResultFile(file);
    }

    /// <summary>Gets or sets the deadline and result-file reporting context.</summary>
    public TestContext TestContext { get; set; }
}