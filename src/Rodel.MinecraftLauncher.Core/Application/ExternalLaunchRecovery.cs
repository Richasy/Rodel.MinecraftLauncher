// Copyright (c) Richasy. All rights reserved.

using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.Application;

/// <summary>A failed external launch's frozen target and currently available recovery choices; reading it never launches a game.</summary>
/// <param name="Request">The original typed task with the exact account and server retained.</param>
/// <param name="Account">The original external identity, without protected credentials.</param>
/// <param name="Code">The recorded authentication or component failure code.</param>
/// <param name="Message">The original sanitized failure message.</param>
/// <param name="Details">Sanitized remote or component evidence.</param>
/// <param name="CanUseCache">Whether an explicit cached attempt currently has all required protected data and component bytes.</param>
public sealed record ExternalLaunchRecovery(LaunchTask Request, IdentityRecord Account, string Code, string Message, JsonNode? Details, bool CanUseCache);

public sealed partial class LauncherClient
{
    /// <summary>Reads an instance-owned failed task/session and retains its original identity and destination for an explicit recovery attempt.</summary>
    /// <param name="instanceId">The instance whose failure is being inspected.</param>
    /// <param name="sessionId">The observed session, if preparation reached session creation.</param>
    /// <param name="jobId">The original typed launch task, or null to read its identifier from the session.</param>
    /// <returns>Recovery choices for an external authentication failure, or null for another failure or an active game.</returns>
    public ExternalLaunchRecovery? GetExternalLaunchRecovery(string instanceId, string? sessionId = null, string? jobId = null)
    {
        var instance = Instances.Get(instanceId);
        if (Sessions.HasActiveGame(instance.GameDirectory)) return null;
        var session = sessionId is null ? null : Sessions.Get(sessionId);
        if (session is not null && session.InstanceId != instanceId)
            throw new ArgumentException("The session does not belong to the requested instance.", nameof(sessionId));
        jobId ??= session?.JobId;
        if (jobId is null || Tasks.GetRequest(jobId) is not LaunchTask request || request.InstanceId != instanceId) return null;
        var job = Jobs.Get(jobId);
        if (job.Status is not ("failed" or "interrupted" or "canceled")) return null;
        var error = job.Failure?["error"];
        var code = session?.ErrorCode ?? error?["code"]?.ToString() ?? string.Empty;
        if (!code.StartsWith("AUTH_", StringComparison.Ordinal)) return null;
        var accountId = session?.AccountId ?? request.Options.AccountId;
        if (accountId is null) return null;
        var account = Identities.Get(accountId);
        if (account.Type != "external") return null;
        var options = request.Options with { AccountId = accountId, InheritIdentity = false, InheritServer = false };
        return new(request with { Options = options }, account, code, session?.Error ?? error?["message"]?.ToString() ?? job.Error ?? string.Empty,
            session?.ErrorDetails?.DeepClone() ?? error?["details"]?.DeepClone(),
            code is "AUTH_NETWORK_ERROR" or "AUTH_TIMEOUT" && Identities.CanUseCachedExternalSession(accountId));
    }
}