// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Serialization;

namespace Rodel.MinecraftLauncher.Core.Launch;

/// <summary>The closed choice between a launch plan and an observed game session.</summary>
[JsonDerivedType(typeof(LaunchPlan))]
[JsonDerivedType(typeof(SessionStatus))]
public interface ILaunchResult;

/// <summary>A stop observation or request, distinct from proof that the game has exited.</summary>
/// <param name="Id">Observed session identifier.</param>
/// <param name="StopRequested">Whether a stop signal was issued.</param>
/// <param name="Message">Explanation of the actual stop outcome.</param>
[JsonDerivedType(typeof(InactiveSessionStopResult))]
[JsonDerivedType(typeof(RequestedSessionStopResult))]
public abstract record SessionStopResult(string Id, bool StopRequested, string Message);

/// <summary>Reports a session already known to be inactive.</summary>
/// <param name="Id">Observed session.</param>
/// <param name="Status">Observed terminal or interrupted state.</param>
/// <param name="Message">Explanation that no signal was sent.</param>
public sealed record InactiveSessionStopResult(string Id, string? Status, string Message)
    : SessionStopResult(Id, false, Message);

/// <summary>Reports a close or termination request and verified window ownership.</summary>
/// <param name="Id">Target session.</param>
/// <param name="StopRequested">Whether the request was accepted.</param>
/// <param name="Forced">Whether the recorded process tree was terminated.</param>
/// <param name="Warning">Failure to persist ancillary stop intent.</param>
/// <param name="Method">Actual signal method.</param>
/// <param name="WindowProcessId">Verified process owning the target window.</param>
/// <param name="WindowOwner">Recorded-process or descendant ownership.</param>
/// <param name="Message">Outcome explanation.</param>
public sealed record RequestedSessionStopResult(string Id, bool StopRequested, bool Forced, string? Warning,
    string Method, int? WindowProcessId, string? WindowOwner, string Message) : SessionStopResult(Id, StopRequested, Message);