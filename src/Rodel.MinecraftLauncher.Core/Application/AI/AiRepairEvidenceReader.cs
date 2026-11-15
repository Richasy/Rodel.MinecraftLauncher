// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application.AI;

/// <summary>Reads known repair evidence from an immutable original diagnostic document without rewriting its unknown fields.</summary>
internal sealed class AiRepairEvidenceReader(JsonObject document)
{
    internal string? ProblemCode => document["problem"]?["code"]?.ToString();
    internal int? RequiredJavaMajor => document["problem"]?["requiredJavaMajor"]?.GetValue<int>()
        ?? document["details"]?["requiredJavaMajor"]?.GetValue<int>();
    internal bool Contains(string value, StringComparison comparison = StringComparison.Ordinal)
        => document.ToJsonString().Contains(value, comparison);
    internal string FailureSignature => Normalize(document["problem"]?["evidence"]?.ToString()
        ?? document["problem"]?["reportedError"]?["exception"]?.ToString()
        ?? document["problem"]?["reportedError"]?["message"]?.ToString() ?? string.Empty);

    internal static string Signature(LaunchDiagnosisProblem? problem)
        => Normalize(problem?.Evidence ?? problem?.ReportedError?.ExceptionText ?? problem?.ReportedError?.Message ?? string.Empty);

    private static string Normalize(string text)
    {
        text = text.Split('\n')[0].Trim();
        while (text.StartsWith('[') && text.IndexOf(']') is var end && end >= 0)
            text = text[(end + 1)..].TrimStart();
        return text.TrimStart(':', ' ');
    }
}