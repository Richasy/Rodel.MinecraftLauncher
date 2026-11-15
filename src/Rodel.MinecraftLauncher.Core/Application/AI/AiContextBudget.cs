// Copyright (c) Richasy. All rights reserved.

using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Assistant;
using Rodel.MinecraftLauncher.Core.Storage;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Owns per-service/model token calibration; profile files contain numeric observations, never prompts or credentials.</summary>
internal sealed class AiContextBudget : IDisposable
{
    private readonly AiServiceSnapshot _snapshot;
    private readonly AiTokenCounter _counter;
    private readonly string _profilePath;
    private double _factor = 1;
    private int _samples;

    /// <summary>Opens the current model's numeric calibration profile and provider counting transport.</summary>
    public AiContextBudget(LauncherPaths paths, AiServiceSnapshot snapshot, Uri? endpoint)
    {
        _snapshot = snapshot;
        var catalog = AiPublicModelCatalog.For(Path.Combine(paths.Root, "ai", "model-catalog"));
        _counter = new(snapshot.ProviderContext(), endpoint, catalog.Read(refresh: endpoint is { IsLoopback: false } && !endpoint.Host.EndsWith(".invalid", StringComparison.Ordinal)));
        _profilePath = Path.Combine(paths.Root, "ai", "token-calibration", snapshot.Service.Id + "-" + snapshot.Service.Revision + "-" + snapshot.Model!.Id + ".json");
        try
        {
            if (File.Exists(_profilePath))
            {
                var profile = JsonFiles.ReadObject(_profilePath);
                var factor = profile["factor"]?.GetValue<double>();
                if (factor is > 0 and < 100 && double.IsFinite(factor.Value))
                {
                    _factor = factor.Value;
                    _samples = Math.Max(0, profile["samples"]?.GetValue<int>() ?? 0);
                }
            }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException) { }
    }

    /// <summary>Measures this exact request using server counts where supported, with calibrated Tiktoken as the fallback.</summary>
    public Task<AiTokenMeasurement> MeasureAsync(JsonArray history, IReadOnlyList<AIFunctionDeclaration> tools, int? outputTokens, CancellationToken ct)
        => _counter.MeasureAsync(AiChatHistory.Read(history), tools, outputTokens, _factor, ct, _samples > 0);

    /// <summary>Uses the same actual model capacity for one-shot generation without counting or recording its prompt.</summary>
    public Task<int?> GetOutputLimitAsync(CancellationToken ct) => _counter.GetOutputLimitAsync(ct);

    /// <summary>Fits the local approximation to actual input usage from the same request; metadata write failure does not fail the answer.</summary>
    public async Task ObserveAsync(JsonArray history, IReadOnlyList<AIFunctionDeclaration> tools, int? outputTokens, UsageDetails? usage, CancellationToken ct)
    {
        if (usage?.InputTokenCount is not > 0) return;
        if (AiChatHistory.Read(history).Any(message => message.Contents.OfType<DataContent>().Any())) return;
        var actual = usage.InputTokenCount.Value;
        var local = AiTokenCounter.Estimate(_snapshot.ProviderContext(), AiChatHistory.Read(history), tools, outputTokens);
        if (local is null or <= 0) return;
        var sample = (double)actual / local.Value;
        if (!double.IsFinite(sample) || sample is <= 0 or >= 100) return;
        _factor = _samples == 0 ? sample : (_factor * .75) + (sample * .25);
        _samples = Math.Min(_samples + 1, 1000000);
        try
        {
            await JsonFiles.WriteAsync(_profilePath, new JsonObject
            {
                ["schemaVersion"] = 1,
                ["tokenizer"] = "tiktoken_o200k_base",
                ["factor"] = _factor,
                ["samples"] = _samples,
                ["updatedUtc"] = DateTimeOffset.UtcNow.ToString("O")
            }, ct).ConfigureAwait(false);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Disposes the owned provider metadata/counting connection.</summary>
    public void Dispose() => _counter.Dispose();
}