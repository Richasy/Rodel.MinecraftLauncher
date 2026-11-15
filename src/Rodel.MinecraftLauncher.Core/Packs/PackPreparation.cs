// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Java;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Packs;

/// <summary>The closed set of pack command outcomes, serialized only at task and command boundaries.</summary>
[JsonDerivedType(typeof(PackState))]
[JsonDerivedType(typeof(PackReview))]
[JsonDerivedType(typeof(OnlinePackSelection))]
[JsonDerivedType(typeof(OnlinePackPlan))]
public abstract record PackResult;

/// <summary>An online selection reported without downloading or changing an instance.</summary>
public sealed record OnlinePackPlan : OnlinePackSelection
{
    /// <summary>Gets the existing target, or null for a prospective import.</summary>
    public string? InstanceId { get; init; }
    /// <summary>Gets the planned canonical pack operation.</summary>
    public required string Action { get; init; }
}

/// <summary>Ordered pack preparation state suitable for durable task progress and a retained result.</summary>
public sealed record PackPreparation
{
    /// <summary>Gets or sets running, blocked, canceled, failed or the actual completion status.</summary>
    public string Status { get; set; } = "running";
    /// <summary>Gets or sets the retained instance once content import succeeds.</summary>
    public string? InstanceId { get; set; }
    /// <summary>Gets the ordered files, runtime and Java steps.</summary>
    public required IReadOnlyList<PackPreparationStep> Steps { get; init; }
    /// <summary>Gets or sets the active step, or null after processing stops.</summary>
    public string? ActiveStep { get; set; }

    /// <summary>Copies mutable workflow and step state; completed step results are no longer mutated by preparation and remain shared.</summary>
    internal PackPreparation Snapshot() => new()
    {
        Status = Status,
        InstanceId = InstanceId,
        ActiveStep = ActiveStep,
        Steps = [.. Steps.Select(step => new PackPreparationStep
        {
            Id = step.Id,
            Status = step.Status,
            Error = step.Error,
            MissingCount = step.MissingCount,
            Acquisition = step.Acquisition,
            Runtime = step.Runtime,
            Java = step.Java
        })]
    };
}

/// <summary>A typed preparation step with a result selected from the known pack preparation domains.</summary>
[JsonConverter(typeof(PackPreparationStepConverter))]
public sealed record PackPreparationStep
{
    /// <summary>Gets the stable files, runtime or Java step identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets or sets the actual step lifecycle status.</summary>
    public string Status { get; set; } = "pending";
    /// <summary>Gets or sets the original execution failure.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Error { get; set; }
    /// <summary>Gets or sets the selected missing declaration count for a blocked files step.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? MissingCount { get; set; }
    /// <summary>Gets or sets the acquisition result for the files step.</summary>
    [JsonIgnore]
    public PackAcquisition? Acquisition { get; set; }
    /// <summary>Gets or sets the runtime preparation and committed selection for this step.</summary>
    [JsonIgnore]
    public RuntimePreparationResult? Runtime { get; set; }
    /// <summary>Gets or sets the Java preparation result.</summary>
    [JsonIgnore]
    public PackJavaPreparation? Java { get; set; }
}

internal sealed class PackPreparationStepConverter : JsonConverter<PackPreparationStep>
{
    /// <inheritdoc/>
    public override PackPreparationStep Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var document = JsonNode.Parse(ref reader)!.AsObject();
        var step = new PackPreparationStep
        {
            Id = document["id"]!.GetValue<string>(),
            Status = document["status"]!.GetValue<string>(),
            Error = document["error"]?.GetValue<string>(),
            MissingCount = document["missingCount"]?.GetValue<int>()
        };
        if (document["result"] is { } result)
        {
            if (step.Id == "files") step.Acquisition = result.Deserialize(PackJsonContext.Default.PackAcquisition);
            else if (step.Id == "java") step.Java = result.Deserialize(PackJsonContext.Default.PackJavaPreparation);
            else if (step.Id == "runtime") step.Runtime = result.Deserialize(RuntimeJsonContext.Default.RuntimePreparationResult);
        }

        return step;
    }

    /// <inheritdoc/>
    public override void Write(Utf8JsonWriter writer, PackPreparationStep value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        writer.WriteString("id", value.Id);
        writer.WriteString("status", value.Status);
        if (value.Error is not null) writer.WriteString("error", value.Error);
        if (value.MissingCount is { } missing) writer.WriteNumber("missingCount", missing);
        if (value.Acquisition is not null)
        {
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, value.Acquisition, PackJsonContext.Default.PackAcquisition);
        }
        else if (value.Runtime is not null)
        {
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, value.Runtime, RuntimeJsonContext.Default.RuntimePreparationResult);
        }
        else if (value.Java is not null)
        {
            writer.WritePropertyName("result");
            JsonSerializer.Serialize(writer, value.Java, PackJsonContext.Default.PackJavaPreparation);
        }

        writer.WriteEndObject();
    }
}

/// <summary>Describes selected Java plus a recoverable preparation warning.</summary>
public sealed record PackJavaPreparation
{
    /// <summary>Gets the actual selected and inspected Java candidates.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JavaSelectionReport? Java { get; init; }
    /// <summary>Gets a recoverable acquisition failure while existing Java remains usable.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaWarning { get; init; }
}

/// <summary>A preparation failure associated with its actual workflow stage.</summary>
/// <param name="Code">The normal launcher error code.</param>
/// <param name="Message">The original failure explanation.</param>
/// <param name="Stage">The runtime or Java step which failed.</param>
/// <param name="NetworkReason">The network failure category, when present.</param>
/// <param name="HttpStatus">The HTTP failure status, when present.</param>
public sealed record PackPreparationFailure(string Code, string Message, string Stage,
    string? NetworkReason = null, int? HttpStatus = null)
{
    internal static PackPreparationFailure From(Exception exception, string stage)
    {
        var error = LauncherOperationException.CreateResult(exception).Error!;
        return new(error.Code, error.Message, stage, error.NetworkReason, error.HttpStatus);
    }
}