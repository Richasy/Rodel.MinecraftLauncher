// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Instances;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Servers;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ServerCommands
{
    internal LauncherCommand Connect { get; }

    internal ServerCommands(Command root, IReadOnlyList<Option> launchOptions)
    {
        var server = Group(root, "server", S.GroupServerDescription);
        Add(server, "list", S.ServerListDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.ListAsync(ct).ConfigureAwait(false), ServerJsonContext.Default.IReadOnlyListServerFavoriteEntry));
        Add(server, "show", S.ServerShowDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.GetAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavorite), [A.Target]);
        Add(server, "add", S.ServerAddDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.AddAsync(result.GetRequiredValue(A.Name), result.GetRequiredValue(A.Address), result.GetValue(O.Id), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavorite), [A.Name, A.Address], [O.Id]);
        Add(server, "remove", S.ServerRemoveDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.RemoveAsync(result.GetRequiredValue(A.Address), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavoriteRemoval), [A.Address]);
        Add(server, "bind", S.ServerBindDescription, async (app, result, ct) => result.GetValue(O.Dedicated)
            ? JsonSerializer.SerializeToNode(await app.Client.Servers.SetClientOwnerAsync(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Target), result.GetValue(O.Preferred), ct).ConfigureAwait(false), InstanceJsonContext.Default.InstanceSummary)
            : JsonSerializer.SerializeToNode(await app.Client.Servers.AssociateAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Instance), result.GetValue(O.Preferred), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavorite), [A.Target, A.Instance], [O.Preferred, O.Dedicated]);
        Add(server, "release-client", S.ServerReleaseClientDescription, async (app, result, ct) => await app.Client.Servers.SetClientOwnerAsync(result.GetRequiredValue(A.Instance), null, ct: ct).ConfigureAwait(false), InstanceJsonContext.Default.InstanceSummary, [A.Instance]);
        Add(server, "unbind", S.ServerUnbindDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.DisassociateAsync(result.GetRequiredValue(A.Target), result.GetRequiredValue(A.Instance), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavorite), [A.Target, A.Instance]);
        Add(server, "set-instances", S.ServerSetInstancesDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.SetInstancesAsync(result.GetRequiredValue(A.Target), result.GetValue(A.Names) ?? [], result.GetValue(O.PreferredInstance), ct).ConfigureAwait(false), ServerJsonContext.Default.ServerFavorite), [A.Target, A.Names], [O.PreferredInstance]);
        Add(server, "status", S.ServerStatusDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await app.Client.Servers.StatusAsync(result.GetRequiredValue(A.Address), result.GetValue(O.Timeout) ?? 5000, ct).ConfigureAwait(false), ServerJsonContext.Default.ServerStatus), [A.Address], [O.Timeout]);
        Add(server, "diagnose", S.ServerDiagnoseDescription, async (app, result, ct) => await app.Client.ServerConnections.ReadAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), ServerConnectionJsonContext.Default.ServerConnectionReport, [A.Target]);
        Add(server, "resolve", S.ServerResolveDescription, async (app, result, ct) => await ResolveAsync(app, result, ct).ConfigureAwait(false), [A.Address], [O.Timeout]);
        Connect = Add(server, "connect", S.ServerConnectDescription, async (app, result, ct) =>
        {
            var request = await CreateLaunchRequestAsync(app.Client, result, ct).ConfigureAwait(false);
            return await app.Client.LaunchAsync(request.InstanceId, request.Options, ct).ConfigureAwait(false);
        }, LaunchJsonContext.Default.ILaunchResult, [A.Target, A.OptionalAddress], [.. launchOptions, O.Instance]);
        Connect.AsyncTaskFactory = async (client, result, ct) => await CreateLaunchRequestAsync(client, result, ct).ConfigureAwait(false);
    }

    private static Task<LaunchTask> CreateLaunchRequestAsync(LauncherClient client, ParseResult args, CancellationToken ct) =>
        args.GetValue(O.ServerAddress) is { } concreteAddress
            ? client.CreateServerLaunchAsync(concreteAddress, args.GetValue(O.Instance) ?? args.GetRequiredValue(A.Target), LaunchCommands.ParseOptions(args), ct)
            : args.GetValue(A.OptionalAddress) is { } address
                ? client.CreateServerLaunchAsync(address, args.GetRequiredValue(A.Target), LaunchCommands.ParseOptions(args), ct)
                : client.CreateServerLaunchAsync(args.GetRequiredValue(A.Target), args.GetValue(O.Instance), LaunchCommands.ParseOptions(args), ct);

    private static async Task<JsonObject> ResolveAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(args.GetValue(O.Timeout) ?? 5000);
        return JsonSerializer.SerializeToNode(await new ServerService(app.Paths).ResolveAsync(args.GetRequiredValue(A.Address), deadline.Token).ConfigureAwait(false), ServerJsonContext.Default.ServerResolution)!.AsObject();
    }
}