// Copyright (c) Richasy. All rights reserved.

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>Advisory heap sizing based on observed available physical memory and explicit bounds.</summary>
/// <param name="AvailableMb">Observed available physical memory, or null when the query failed.</param>
/// <param name="ReserveMb">Configured reserve.</param>
/// <param name="MinimumMb">Configured preferred minimum.</param>
/// <param name="MaximumMb">Configured maximum.</param>
/// <param name="Java32Bit">Whether the selected runtime has a 32-bit address space.</param>
/// <param name="SuggestedMb">Suggested maximum heap.</param>
/// <param name="Source">Memory-query or fallback evidence.</param>
/// <param name="Advisory">Always true; advice never blocks a user configuration.</param>
/// <param name="Warning">Memory-query warning, if any.</param>
public record MemoryRecommendation(long? AvailableMb, int ReserveMb, int MinimumMb, int MaximumMb, bool Java32Bit, int SuggestedMb, string Source, bool Advisory, string? Warning);

/// <summary>Explains the heap arguments actually selected for one launch without changing model processes.</summary>
public sealed record MemorySelectionReport : MemoryRecommendation
{
    /// <summary>Combines memory advice with the authority and value of the chosen heap arguments.</summary>
    public MemorySelectionReport(MemoryRecommendation recommendation, string mode, string selectionSource, int? effectiveMb, string? customXmx)
        : this(recommendation.AvailableMb, recommendation.ReserveMb, recommendation.MinimumMb, recommendation.MaximumMb, recommendation.Java32Bit,
            recommendation.SuggestedMb, recommendation.Source, recommendation.Advisory, recommendation.Warning, mode, selectionSource, effectiveMb, customXmx)
    {
    }

    /// <summary>Restores the flattened memory-selection contract through source-generated metadata.</summary>
    [System.Text.Json.Serialization.JsonConstructor]
    public MemorySelectionReport(long? availableMb, int reserveMb, int minimumMb, int maximumMb, bool java32Bit, int suggestedMb, string source, bool advisory, string? warning,
        string mode, string selectionSource, int? effectiveMb, string? customXmx)
        : base(availableMb, reserveMb, minimumMb, maximumMb, java32Bit, suggestedMb, source, advisory, warning)
    {
        Mode = mode;
        SelectionSource = selectionSource;
        EffectiveMb = effectiveMb;
        CustomXmx = customXmx;
    }

    /// <summary>Gets the resolved manual or automatic mode.</summary>
    public string Mode { get; }
    /// <summary>Gets the explicit, custom-JVM, automatic or configured authority.</summary>
    public string SelectionSource { get; }
    /// <summary>Gets the effective heap size when its syntax was recognized.</summary>
    public int? EffectiveMb { get; }
    /// <summary>Gets the custom user Xmx argument, if present.</summary>
    public string? CustomXmx { get; }
    /// <summary>Gets false; heap advice does not reconfigure external model processes.</summary>
    public bool ModelProcessesChanged { get; } = false;
}