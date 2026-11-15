// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Servers;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A matched diagnostic rule and its bounded evidence, not a proven root cause.</summary>
/// <param name="Code">Stable diagnostic code.</param>
/// <param name="Evidence">Matched source line.</param>
/// <param name="EvidenceTruncated">Whether the evidence line was bounded.</param>
/// <param name="Suggestion">Suggested inspection or recovery action.</param>
/// <param name="Confidence">Optional confidence classification.</param>
/// <param name="Source">Optional source log stream.</param>
/// <param name="Scope">Optional attribution boundary for historical evidence.</param>
public sealed record DiagnosticFinding(string Code, string Evidence, bool EvidenceTruncated, string Suggestion,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Confidence = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Source = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Scope = null);

/// <summary>Recognized header fields from a crash report; missing headers remain absent.</summary>
public sealed class CrashDetails
{
    /// <summary>Gets the report's description header.</summary>
    [JsonPropertyName("Description"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Description { get; set; }
    /// <summary>Gets the report's game-version header.</summary>
    [JsonPropertyName("Minecraft Version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MinecraftVersion { get; set; }
    /// <summary>Gets the report's Java-version header.</summary>
    [JsonPropertyName("Java Version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaVersion { get; set; }
    /// <summary>Gets the report's VM-version header.</summary>
    [JsonPropertyName("Java VM Version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaVmVersion { get; set; }
    /// <summary>Gets the report's operating-system header.</summary>
    [JsonPropertyName("Operating System"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? OperatingSystem { get; set; }
    /// <summary>Gets the report's launched-profile header.</summary>
    [JsonPropertyName("Launched Version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LaunchedVersion { get; set; }
    /// <summary>Gets the Java major inferred from a reported class-file version.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? RequiredJavaMajor { get; set; }
    /// <summary>Gets the class-file-version evidence line.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ClassVersionEvidence { get; set; }
    /// <summary>Gets the class explicitly named in incompatible bytecode evidence.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ResponsibleClass { get; set; }
    /// <summary>Gets whether the Java requirement came from a loader dependency declaration.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? JavaRequirementKind { get; set; }
}

/// <summary>Common source evidence for a loader-reported dependency requirement.</summary>
/// <param name="Evidence">Original bounded evidence line.</param>
[JsonDerivedType(typeof(FabricDependencyEvidence))]
[JsonDerivedType(typeof(ModDependencyEvidence))]
public abstract record DependencyEvidence(string Evidence);

/// <summary>A specifically recognized Fabric dependency requirement.</summary>
/// <param name="Kind">Whether the loader explicitly said the dependency is missing.</param>
/// <param name="DependencyId">Required dependency identifier.</param>
/// <param name="RequestedBy">Requesting mod identifier.</param>
/// <param name="Requirement">Original version requirement text.</param>
/// <param name="Evidence">Original source line.</param>
public sealed record FabricDependencyEvidence(string Kind, string DependencyId, string RequestedBy, string Requirement, string Evidence) : DependencyEvidence(Evidence);

/// <summary>A less specific dependency line retaining explicit unknown values.</summary>
/// <param name="Evidence">Original source line.</param>
/// <param name="ModName">Recognized mod display name.</param>
/// <param name="ModId">Recognized mod identifier.</param>
/// <param name="RequestedBy">Recognized requesting mod identifier.</param>
/// <param name="Requirement">Recognized requirement text.</param>
/// <param name="ActualVersion">Recognized actual version.</param>
public sealed record ModDependencyEvidence(string Evidence, string? ModName, string? ModId, string? RequestedBy, string? Requirement, string? ActualVersion) : DependencyEvidence(Evidence);

/// <summary>Crash-report evidence and optional bounded file-read metadata.</summary>
public sealed record CrashAnalysis
{
    /// <summary>Gets recognized report headers.</summary>
    public required CrashDetails Details { get; init; }
    /// <summary>Gets matched advisory findings.</summary>
    public required IReadOnlyList<DiagnosticFinding> Findings { get; init; }
    /// <summary>Gets recognized dependency requirement evidence.</summary>
    public required IReadOnlyList<DependencyEvidence> DependencyEvidence { get; init; }
    /// <summary>Gets the scope and confidence limitation of this analysis.</summary>
    public string Interpretation { get; init; } = "Reported exceptions and loader requirements are evidence and repair suggestions, not proof that every named mod caused the crash.";
    /// <summary>Gets the source file path when analyzing a file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Path { get; init; }
    /// <summary>Gets the bytes actually read from the file.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? BytesRead { get; init; }
    /// <summary>Gets the observed source file length.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public long? TotalBytes { get; init; }
    /// <summary>Gets whether the bounded file read omitted bytes.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? Truncated { get; init; }
    /// <summary>Gets the decoder's canonical name.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? Encoding { get; init; }
    /// <summary>Gets a decoder fallback warning, when needed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? EncodingWarning { get; init; }
}

/// <summary>An independently unreadable diagnostic candidate.</summary>
/// <param name="Path">Candidate report or directory.</param>
/// <param name="Error">Original read failure.</param>
public sealed record DiagnosticReadWarning(string Path, string Error);

/// <summary>Original launcher preparation failure or bounded session log and time-associated crash-report evidence.</summary>
/// <param name="SessionId">Source session.</param>
/// <param name="Status">Observed session status.</param>
/// <param name="ExitCode">Recorded exit code, if available.</param>
/// <param name="Findings">Deduplicated advisory findings.</param>
/// <param name="DependencyEvidence">Loader requirement evidence from logs.</param>
/// <param name="CrashReports">Bounded analyses of candidate crash reports.</param>
/// <param name="ReportWarnings">Candidate read failures retained independently.</param>
/// <param name="LogRead">Per-stream byte-cursor and truncation evidence.</param>
/// <param name="ReportAssociation">Explicit limits of the file association.</param>
/// <param name="Interpretation">Explicit limits of the diagnosis.</param>
public sealed record SessionDiagnosis(string SessionId, string? Status, int? ExitCode, IReadOnlyList<DiagnosticFinding> Findings, IReadOnlyList<DependencyEvidence> DependencyEvidence,
    IReadOnlyList<CrashAnalysis> CrashReports, IReadOnlyList<DiagnosticReadWarning> ReportWarnings, IReadOnlyDictionary<string, LogStreamSummary> LogRead,
    string ReportAssociation = "Files written within the session time window are candidates, not proof of which process wrote them.",
    string Interpretation = "Matched evidence suggests the listed causes; it does not establish every underlying cause.") : ILaunchDiagnosis
{
    /// <summary>Gets the requested owning instance only when reading through the instance-scoped launch diagnostic API.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? InstanceId { get; init; }
    /// <summary>Gets the retained launcher preparation failure when this session failed before a game process existed; its sanitized error details are independent of game logs.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Launch.SessionStatus? PreparationFailure { get; init; }
    /// <summary>Gets original bounded errors reported by the game or loader.</summary>
    public IReadOnlyList<ReportedLaunchError> ReportedErrors { get; init; } = [];
    /// <summary>Gets errors attributable to the current process or matching connection attempt.</summary>
    public IReadOnlyList<ReportedLaunchError> CurrentReportedErrors { get; init; } = [];
    /// <summary>Gets byte-cursor evidence for the structured error read.</summary>
    public IReadOnlyDictionary<string, LogStreamSummary> ErrorLogRead { get; init; } = new Dictionary<string, LogStreamSummary>();
    /// <summary>Gets recoverable structured log read warnings.</summary>
    public IReadOnlyList<string> ErrorLogWarnings { get; init; } = [];
    /// <summary>Gets recognized report headers and Java requirements.</summary>
    public CrashDetails? Details { get; init; }
    /// <summary>Gets the Java major recorded for this launch.</summary>
    public int? SelectedJavaMajor { get; init; }
    /// <summary>Gets whether a game process currently owns this game directory.</summary>
    public bool GameActive { get; init; }
    /// <summary>Gets the bounded stdout and stderr excerpts used by the diagnosis.</summary>
    public LaunchLogExcerpt? LogExcerpt { get; init; }
    /// <summary>Gets advisory connection-attempt evidence, without equating launch to joining.</summary>
    public ServerConnectionReport? ServerConnection { get; init; }
    /// <summary>Gets the actionable interpretation retaining the original reported error.</summary>
    public LaunchDiagnosisProblem? Problem { get; init; }
}

/// <summary>A launch diagnostic is either actual session evidence or an instance's preparation-task failure.</summary>
[JsonDerivedType(typeof(SessionDiagnosis))]
[JsonDerivedType(typeof(PreparationDiagnosis))]
public interface ILaunchDiagnosis
{
    /// <summary>Gets the explicitly requested owning instance when present.</summary>
    public string? InstanceId { get; }
    /// <summary>Gets whether a process currently owns the game directory.</summary>
    public bool GameActive { get; }
    /// <summary>Gets the advisory interpretation of the original failure evidence.</summary>
    public LaunchDiagnosisProblem? Problem { get; }
}

/// <summary>Preserves preparation failures independently of whether any game process was created.</summary>
/// <param name="InstanceId">The requested instance.</param>
/// <param name="Job">The exact original preparation record including its failure details.</param>
/// <param name="GameActive">The observed game-directory ownership.</param>
/// <param name="Findings">The preparation branch's advisory findings.</param>
/// <param name="Problem">The original job failure and advisory recovery route.</param>
public sealed record PreparationDiagnosis(string InstanceId, JobStatus? Job, bool GameActive, IReadOnlyList<DiagnosticFinding> Findings, LaunchDiagnosisProblem Problem) : ILaunchDiagnosis
{
    /// <summary>Gets whether diagnosis changed no instance files or jobs.</summary>
    public bool ReadOnly { get; } = true;
}

/// <summary>The exact bounded stdout and stderr text used by launch diagnosis.</summary>
/// <param name="Stdout">Observed standard-output lines.</param>
/// <param name="Stderr">Observed standard-error lines.</param>
public sealed record LaunchLogExcerpt(IReadOnlyList<string> Stdout, IReadOnlyList<string> Stderr);

/// <summary>An original reported error, distinguishing parsed logs, raw process output and source-specific failures.</summary>
/// <param name="Message">The original bounded message.</param>
/// <param name="Level">The reported severity or UNKNOWN.</param>
[JsonDerivedType(typeof(ParsedLaunchError))]
[JsonDerivedType(typeof(RawProcessLaunchError))]
[JsonDerivedType(typeof(SourceLaunchError))]
public abstract record ReportedLaunchError(string? Message, string Level)
{
    /// <summary>Gets exception continuation only when the source actually supplied it.</summary>
    [JsonIgnore]
    public string? ExceptionText => (this as ParsedLaunchError)?.Exception;
    /// <summary>Gets the attributed log scope independently of source logger identity.</summary>
    [JsonIgnore]
    public string? AttributionScope => this switch { ParsedLaunchError parsed => parsed.Scope, RawProcessLaunchError raw => raw.Scope, _ => null };
    /// <summary>Gets whether raw process failure, rather than a parsed error level, established the failure.</summary>
    [JsonIgnore]
    public bool FromFailedProcess => this is RawProcessLaunchError;
}

/// <summary>A parsed log error with original nullable parser fields and explicit attribution.</summary>
/// <param name="Message">The parser's bounded message.</param>
/// <param name="Level">The normalized ERROR or FATAL severity.</param>
public sealed record ParsedLaunchError(string? Message, string Level) : ReportedLaunchError(Message, Level)
{
    /// <summary>Gets the original exception continuation.</summary>
    public string? Exception { get; init; }
    /// <summary>Gets the original logger name.</summary>
    public string? Logger { get; init; }
    /// <summary>Gets the original timestamp value, including caller-supplied continuation metadata without coercion.</summary>
    public JsonNode? Timestamp { get; init; }
    /// <summary>Gets the source stream.</summary>
    public string? Stream { get; init; }
    /// <summary>Gets the parser format.</summary>
    public string? Format { get; init; }
    /// <summary>Gets whether this is a pending parser snapshot; absent parser state remains explicit null.</summary>
    public bool? Pending { get; init; }
    /// <summary>Gets the original parser warning.</summary>
    public string? Warning { get; init; }
    /// <summary>Gets process, connection-attempt or unattributed-session scope.</summary>
    public required string Scope { get; init; }
}

/// <summary>Original stderr retained after an abnormal process exit when no structured stderr error exists.</summary>
/// <param name="Message">The bounded original stderr text.</param>
public sealed record RawProcessLaunchError(string? Message) : ReportedLaunchError(Message, "UNKNOWN")
{
    /// <summary>Gets the actual stderr source.</summary>
    public string Stream { get; } = "stderrLog";
    /// <summary>Gets the unparsed source format.</summary>
    public string Format { get; } = "raw";
    /// <summary>Gets the process-level attribution.</summary>
    public string Scope { get; } = "game-process";
    /// <summary>Gets the observed abnormal-exit evidence.</summary>
    public bool ProcessFailed { get; } = true;
}

/// <summary>A bounded source-reported failure without invented parser or process attribution fields.</summary>
/// <param name="Message">The original source explanation.</param>
/// <param name="Level">The supplied error level.</param>
/// <param name="Source">The actual client-log, session-error, job-error or launcher-error source.</param>
public sealed record SourceLaunchError(string? Message, string Level, string Source) : ReportedLaunchError(Message, Level);

/// <summary>The common advisory recovery identity, never proof of world entry or a permission grant.</summary>
/// <param name="Code">The classified failure code.</param>
/// <param name="Evidence">The original bounded source evidence.</param>
/// <param name="Action">The advisory recovery route.</param>
[JsonDerivedType(typeof(LaunchDiagnosisProblem))]
[JsonDerivedType(typeof(StatusQueryProblem))]
[JsonDerivedType(typeof(UnavailableLaunchProblem))]
public abstract record LaunchProblemReport(string Code, string? Evidence, string Action);

/// <summary>A session or preparation diagnostic interpretation, preserving explicitly unknown outcome fields.</summary>
/// <param name="Code">The classified diagnostic code.</param>
/// <param name="Evidence">The original bounded diagnostic evidence.</param>
/// <param name="Action">The advisory recovery route.</param>
public sealed record LaunchDiagnosisProblem(string Code, string? Evidence, string Action) : LaunchProblemReport(Code, Evidence, Action)
{
    /// <summary>Gets the observed failure phase.</summary>
    public required string Phase { get; init; }
    /// <summary>Gets the original source used for presentation.</summary>
    public required string Source { get; init; }
    /// <summary>Gets the classification source, independently of the displayed original error.</summary>
    public string? ClassificationSource { get; init; }
    /// <summary>Gets the actionable original error.</summary>
    public ReportedLaunchError? ReportedError { get; init; }
    /// <summary>Gets fallback session-log evidence when no current error was attributable.</summary>
    public ReportedLaunchError? SessionReportedError { get; init; }
    /// <summary>Gets the actual class named by bytecode evidence.</summary>
    public string? Subject { get; init; }
    /// <summary>Gets bounded registry identifiers only from a failed matching connection.</summary>
    public IReadOnlyList<string>? RegistryEntries { get; init; }
    /// <summary>Gets the Java major actually required by observed evidence.</summary>
    public int? RequiredJavaMajor { get; init; }
    /// <summary>Gets whether the Java requirement came from a loader declaration.</summary>
    public string? JavaRequirementKind { get; init; }
    /// <summary>Gets the Java major actually selected by the launch.</summary>
    public int? SelectedJavaMajor { get; init; }
    /// <summary>Gets the current process ownership observation.</summary>
    public bool? GameActive { get; init; }
    /// <summary>Gets explicit uncertainty about world entry.</summary>
    public bool? Joined { get; }
}

/// <summary>A failed optional status query, not evidence that joining the server is impossible.</summary>
/// <param name="Evidence">The original status query error.</param>
/// <param name="QueryCode">The actual status query failure code.</param>
public sealed record StatusQueryProblem(string? Evidence, string? QueryCode) : LaunchProblemReport("STATUS_QUERY_UNAVAILABLE", Evidence, "edit-connection")
{
    /// <summary>Gets the optional status-query phase.</summary>
    public string Phase { get; } = "status-query";
    /// <summary>Gets the optional server-status source.</summary>
    public string Source { get; } = "server-status";
    /// <summary>Gets explicit uncertainty about game activity.</summary>
    public bool? GameActive { get; }
    /// <summary>Gets explicit uncertainty about world entry.</summary>
    public bool? Joined { get; }
}

/// <summary>A diagnostic read failure kept separate from the underlying game's actual outcome.</summary>
/// <param name="Evidence">The diagnostic read failure explanation.</param>
public sealed record UnavailableLaunchProblem(string? Evidence) : LaunchProblemReport("DIAGNOSIS_UNAVAILABLE", Evidence, "view-details");

internal readonly record struct DiagnosticLogEvent(LogEvent Event, bool? Pending = null);

internal static class DiagnosticLogFields
{
    internal static string? Logger(LogEvent entry) => entry switch { TextLogEvent text => text.Logger, XmlLogEvent xml => xml.Logger, _ => null };
    internal static string? Exception(LogEvent entry) => entry switch { TextLogEvent text => text.Exception, XmlLogEvent xml => xml.Exception, _ => null };
    internal static JsonNode? Timestamp(LogEvent entry) => entry switch
    {
        XmlLogEvent xml => JsonValue.Create(xml.Timestamp),
        TextLogEvent text when text.AdditionalData.TryGetValue("timestamp", out var original) => JsonNode.Parse(original.GetRawText()),
        _ => null
    };
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, Converters = [typeof(DiagnosticJobStatusConverter)])]
[JsonSerializable(typeof(ILaunchDiagnosis))]
[JsonSerializable(typeof(LaunchProblemReport))]
[JsonSerializable(typeof(LaunchDiagnosisProblem))]
[JsonSerializable(typeof(ReportedLaunchError))]
public sealed partial class DiagnosticJsonContext : JsonSerializerContext;

/// <summary>Uses the application's task metadata, including progress-presence rules, for nested preparation evidence.</summary>
internal sealed class DiagnosticJobStatusConverter : JsonConverter<JobStatus>
{
    /// <inheritdoc />
    public override JobStatus Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        => JobStatus.FromStored(JsonNode.Parse(ref reader) as JsonObject
            ?? throw new JsonException("A diagnostic task snapshot must be an object."));

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, JobStatus value, JsonSerializerOptions options)
        => JsonSerializer.Serialize(writer, value, ApplicationJsonContext.Job);
}

/// <summary>Observed Java executable details relevant to launch diagnostics.</summary>
/// <param name="Path">Probed executable path.</param>
/// <param name="Usable">Whether the executable can run.</param>
/// <param name="MajorVersion">Detected Java major version.</param>
public sealed record DiagnosticJava(string Path, bool Usable, int? MajorVersion);

/// <summary>Advisory instance diagnostics, retaining heterogeneous recoverable issue payloads.</summary>
public sealed class InstanceDiagnosis
{
    /// <summary>Gets the diagnosed instance.</summary>
    public required string InstanceId { get; init; }
    /// <summary>Gets its game directory.</summary>
    public required string GameDirectory { get; init; }
    /// <summary>Gets heterogeneous issue details, including original configuration warnings.</summary>
    public JsonArray Issues { get; init; } = [];
    /// <summary>Gets the Java probe when metadata could be resolved.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DiagnosticJava? Java { get; set; }
    /// <summary>Gets launch-planning warnings when a plan was available.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<string>? Warnings { get; set; }
    /// <summary>Gets the number of checked required files.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? CheckedFiles { get; set; }
    /// <summary>Gets whether hash verification was requested and performed.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? HashesVerified { get; set; }
    /// <summary>Gets whether a usable Java executable was found.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public bool? UsableJava { get; set; }
}