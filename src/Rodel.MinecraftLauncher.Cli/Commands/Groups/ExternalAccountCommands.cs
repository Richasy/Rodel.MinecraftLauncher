// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ExternalAccountCommands
{
    private static readonly Argument<string?> Address = new("address") { Arity = ArgumentArity.ZeroOrOne, Description = S.ExternalAddress };
    private static readonly Argument<string> Attempt = new("attempt") { Description = S.ExternalAttempt };
    private static readonly Argument<string> Profile = new("profile") { Description = S.ExternalProfile };
    private static readonly Option<string> Username = new("--username") { Required = true, Description = S.ExternalUsername };
    private static readonly Option<bool> PasswordStdin = new("--password-stdin") { Description = S.ExternalPasswordStdin };
    private static readonly Option<string?> File = new("--file") { Description = S.ExternalComponentFile };

    internal ExternalAccountCommands(Command account)
    {
        var external = Group(account, "external", S.ExternalGroup);
        Add(external, "login", S.ExternalLogin, LoginAsync, [Address], [Username, PasswordStdin, O.Account]);
        Add(external, "device", S.ExternalDevice, async (app, args, ct) => Result(await app.Client.Identities.BeginLittleSkinAsync(args.GetValue(O.Account), ct: ct).ConfigureAwait(false)), options: [O.Account]);
        Add(external, "poll", S.ExternalPoll, async (app, args, ct) =>
        {
            ExternalLoginResult result;
            do { result = await app.Client.Identities.PollLittleSkinAsync(args.GetRequiredValue(Attempt), ct).ConfigureAwait(false); }
            while (args.GetValue(O.Wait) && result.Stage == "device");
            return Result(result);
        }, [Attempt], [O.Wait]);
        Add(external, "show", S.ExternalShow, async (app, args, ct) => Result(await app.Client.Identities.GetExternalLoginAsync(args.GetRequiredValue(Attempt), ct).ConfigureAwait(false)), [Attempt]);
        Add(external, "select", S.ExternalSelect, async (app, args, ct) => Result(await app.Client.Identities.SelectExternalProfileAsync(args.GetRequiredValue(Attempt), args.GetRequiredValue(Profile), ct).ConfigureAwait(false)), [Attempt, Profile]);
        Add(external, "cancel", S.ExternalCancel, async (app, args, ct) =>
        {
            await app.Client.Identities.CancelExternalLoginAsync(args.GetRequiredValue(Attempt), ct).ConfigureAwait(false);
            return new JsonObject { ["canceled"] = args.GetRequiredValue(Attempt) };
        }, [Attempt]);
        var component = Group(external, "component", S.ExternalComponent);
        Add(component, "show", S.ExternalComponentShow, (app, args, ct) => Task.FromResult(JsonSerializer.SerializeToNode(app.Client.Identities.GetExternalLoginComponent(), ApplicationJsonContext.Default.ExternalLoginComponent)));
        Add(component, "prepare", S.ExternalComponentPrepare, async (app, args, ct) => JsonSerializer.SerializeToNode(await app.Client.Identities.PrepareExternalLoginComponentAsync(args.GetValue(File), ct).ConfigureAwait(false), ApplicationJsonContext.Default.ExternalLoginComponent), options: [File]);
    }

    private static JsonNode? Result(ExternalLoginResult result) => JsonSerializer.SerializeToNode(result, ApplicationJsonContext.Default.ExternalLoginResult);

    private static async Task<JsonNode?> LoginAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        if (!app.AllowsInteractiveSecrets || args.GetValue(O.NonInteractive))
            throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", S.ExternalPasswordDirectOnly);
        if (!args.GetValue(PasswordStdin)) throw new LauncherOperationException("AUTH_INTERACTION_REQUIRED", S.ExternalPasswordRequired);
        var password = await Console.In.ReadLineAsync(ct).ConfigureAwait(false);
        var result = await app.Client.Identities.LoginExternalAsync(new()
        {
            Address = args.GetValue(Address),
            Username = args.GetRequiredValue(Username),
            Password = password ?? string.Empty,
            AccountId = args.GetValue(O.Account)
        }, ct).ConfigureAwait(false);
        return Result(result);
    }
}