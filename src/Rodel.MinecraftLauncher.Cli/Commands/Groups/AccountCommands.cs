// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class AccountCommands
{
    internal Command Root { get; }

    internal AccountCommands(Command root)
    {
        var account = Root = Group(root, "account", S.GroupAccountDescription);
        var skinOnly = new Option<bool>("--skin-only") { Description = S.AccountSkinOnlyDescription };
        Add(account, "list", S.AccountListDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Identities.List(), ApplicationJsonContext.Default.IReadOnlyListIdentityEntry)));
        Add(account, "current", S.AccountCurrentDescription, (app, result, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Identities.GetSelection(), ApplicationJsonContext.Default.IdentitySelection)));
        Add(account, "select", S.AccountSelectDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Identities.SelectAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentitySelection), [A.Target]);
        Add(account, "clear", S.AccountClearDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Identities.SelectAsync(null, ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentitySelection));
        Add(account, "add-offline", S.AccountAddOfflineDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Identities.AddOfflineAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentityRecord), [A.Target]);
        Add(account, "login", S.AccountLoginDescription, async (app, result, ct) => await LoginMicrosoftAsync(app, result, ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentityRecord, options: [O.LoginMethod, O.Account]);
        Add(account, "retry-login", S.AccountRetryLoginDescription, async (app, result, ct) => await app.Client.Identities.RetryMicrosoftLoginAsync(ct: ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentityRecord);
        Add(account, "refresh", S.AccountRefreshDescription, async (app, result, ct) => result.GetValue(skinOnly)
            ? await app.Client.Identities.RefreshSkinAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false)
            : await app.Client.Identities.RefreshAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ApplicationJsonContext.Default.IdentityRecord, [A.Target], options: [skinOnly]);
        _ = new ExternalAccountCommands(account);
        Add(account, "remove", S.AccountRemoveDescription, async (app, result, ct) =>
        {
            var id = result.GetRequiredValue(A.Target);
            await app.Client.Identities.RemoveAsync(id, ct).ConfigureAwait(false);
            return new JsonObject { ["removed"] = id };
        }, [A.Target]);
    }

    private static Task<IdentityRecord> LoginMicrosoftAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        if (args.GetValue(O.NonInteractive))
            throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", S.ErrorAccountInteractionRequired);
        var method = args.GetValue(O.LoginMethod) == "device-code" ? MicrosoftLoginMethod.DeviceCode : MicrosoftLoginMethod.Browser;
        if (method == MicrosoftLoginMethod.DeviceCode && app.EventSink is null)
            throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", S.ErrorAccountDeviceCodeEventsRequired);
        return app.Client.Identities.LoginMicrosoftAsync(new MicrosoftLoginOptions
        {
            Method = method,
            AccountId = args.GetValue(O.Account),
            Prompt = new LoginProgress(prompt => app.EventSink?.Invoke(new JsonObject
            {
                ["event"] = "account.sign-in",
                ["stage"] = prompt.Stage,
                ["verificationUri"] = prompt.VerificationUri,
                ["userCode"] = prompt.UserCode
            }))
        }, ct);
    }

    private sealed class LoginProgress(Action<MicrosoftLoginPrompt> report) : IProgress<MicrosoftLoginPrompt>
    {
        /// <summary>Forwards an authentication prompt synchronously to the host's event sink.</summary>
        public void Report(MicrosoftLoginPrompt value) => report(value);
    }
}