using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;

namespace Rodel.MinecraftLauncher.Assistant.Tests;

/// <summary>Scores exact fixture evidence retention, not paraphrase quality, semantic truthfulness or authorization.</summary>
internal sealed class ExactEvidenceEvaluator(IReadOnlyList<string> requiredEvidence) : IEvaluator
{
    /// <summary>Names the narrowly defined, reproducible evidence-retention metric.</summary>
    public const string MetricName = "Exact evidence coverage";
    /// <inheritdoc />
    public IReadOnlyCollection<string> EvaluationMetricNames => [MetricName];

    /// <inheritdoc />
    public ValueTask<EvaluationResult> EvaluateAsync(IEnumerable<ChatMessage> messages, ChatResponse modelResponse,
        ChatConfiguration? chatConfiguration = null, IEnumerable<EvaluationContext>? additionalContext = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requiredEvidence.Count == 0) throw new ArgumentException("Supply independently expected source evidence.");
        var retained = requiredEvidence.Count(value => modelResponse.Text.Contains(value, StringComparison.Ordinal));
        var complete = retained == requiredEvidence.Count;
        var metric = new NumericMetric(MetricName, 100d * retained / requiredEvidence.Count,
            "Exact source-phrase retention only; this is not an LLM judgment or a losslessness guarantee.")
        {
            Interpretation = new(complete ? EvaluationRating.Good : EvaluationRating.Poor, failed: !complete,
                reason: $"{retained}/{requiredEvidence.Count} independently expected source phrases retained.")
        };
        return ValueTask.FromResult(new EvaluationResult(metric));
    }
}