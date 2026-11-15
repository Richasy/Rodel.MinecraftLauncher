// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Installation;
using Rodel.MinecraftLauncher.Core.Launch;
using System.CommandLine;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class LaunchCommands
{
    private static readonly Option<bool> CachedExternal = new("--use-cached-external-session") { Description = S.ExternalCachedLaunch };
    internal static readonly Option<bool?> Bridge = new("--game-bridge") { Description = S.BridgeLaunch };
    internal LauncherCommand Start { get; }
    internal IReadOnlyList<Option> Options { get; }

    internal LaunchCommands(Command root)
    {
        Option[] installation = [O.Version, O.Java, O.Plan];
        static InstanceInstallationOptions InstallationOptions(ParseResult result) => new() { VersionId = result.GetValue(O.Version), JavaPath = result.GetValue(O.Java), PlanOnly = result.GetValue(O.Plan) };
        foreach (var name in new[] { "install", "repair" })
        {
            Add(root, name, S.LaunchInstallRepairDescription, async (app, result, ct) => await app.Client.InstallInstanceAsync(result.GetRequiredValue(A.Instance), InstallationOptions(result), ct).ConfigureAwait(false), InstallationJsonContext.Default.IInstallationResult, [A.Instance], installation)
                .TaskFactory = result => new InstallInstanceTask(result.GetRequiredValue(A.Instance), InstallationOptions(result));
        }

        Option[] launch = [O.Version, O.Java, O.Account, O.Player, O.Memory, O.MemoryMode, O.LogEncoding, O.GameLanguage, O.LoggingCompatibility, O.Width, O.Height, O.Fullscreen, O.World, O.Server, O.ServerAddress, O.NoServer, O.JvmArguments, O.GameArguments, O.Environment, O.Plan, O.Offline, O.Wait, CachedExternal, Bridge];
        Options = launch;
        Start = Add(root, "launch", S.LaunchLaunchDescription, async (app, result, ct) => await app.Client.LaunchAsync(result.GetRequiredValue(A.Instance), ParseOptions(result), ct).ConfigureAwait(false), LaunchJsonContext.Default.ILaunchResult, [A.Instance], launch);
        Start.TaskFactory = result => new LaunchTask(result.GetRequiredValue(A.Instance), ParseOptions(result));
    }

    internal static LaunchOptions ParseOptions(ParseResult args) => new()
    {
        VersionId = args.GetValue(O.Version),
        JavaPath = args.GetValue(O.Java),
        AccountId = args.GetValue(O.Account),
        PlayerName = args.GetValue(O.Player),
        MaxMemoryMb = args.GetValue(O.Memory),
        MemoryMode = args.GetValue(O.MemoryMode),
        LogEncoding = args.GetValue(O.LogEncoding),
        GameLanguage = args.GetValue(O.GameLanguage),
        LoggingCompatibility = args.GetValue(O.LoggingCompatibility),
        Width = args.GetValue(O.Width),
        Height = args.GetValue(O.Height),
        Fullscreen = args.GetValue(O.Fullscreen),
        World = args.GetValue(O.World),
        Server = args.GetValue(O.Server),
        ServerAddress = args.GetValue(O.ServerAddress),
        InheritServer = !args.GetValue(O.NoServer),
        JvmArguments = [.. args.GetValue(O.JvmArguments) ?? []],
        GameArguments = [.. args.GetValue(O.GameArguments) ?? []],
        Environment = (args.GetValue(O.Environment) ?? []).Select(value => value.Split('=', 2)).GroupBy(pair => pair[0]).ToDictionary(group => group.Key, group => group.Last().Length == 2 ? group.Last()[1] : null),
        PlanOnly = args.GetValue(O.Plan),
        Offline = args.GetValue(O.Offline),
        GameBridge = args.GetValue(Bridge),
        UseCachedExternalSession = args.GetValue(CachedExternal),
        WaitForExit = args.GetValue(O.Wait)
    };
}