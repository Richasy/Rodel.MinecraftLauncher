// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using System.CommandLine.Help;
using System.CommandLine.Parsing;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class LauncherCommands
{
    private static readonly ParserConfiguration ParserOptions = new()
    {
        EnablePosixBundling = false,
        ResponseFileTokenReplacer = null
    };

    internal static RootCommand Root { get; }
    internal static Command Help { get; }
    internal static InformationCommands Information { get; }
    internal static InstanceCommands Instances { get; }
    internal static ConfigurationCommands Configuration { get; }
    internal static JavaCommands Java { get; }
    internal static LaunchCommands Launch { get; }
    internal static SessionCommands Sessions { get; }
    internal static LogCommands Logs { get; }
    internal static JobCommands Jobs { get; }
    internal static BackupCommands Backups { get; }
    internal static LoaderCommands Loaders { get; }
    internal static RuntimeCommands Runtime { get; }
    internal static GameSettingsCommands GameSettings { get; }
    internal static ResourcePackCommands ResourcePacks { get; }
    internal static ShaderPackCommands ShaderPacks { get; }
    internal static ContentCommands Content { get; }
    internal static PackCommands Packs { get; }
    internal static WorldCommands Worlds { get; }
    internal static ScreenshotCommands Screenshots { get; }
    internal static MaintenanceCommands Maintenance { get; }
    internal static RepairCommands Repair { get; }
    internal static ServerCommands Servers { get; }
    internal static AccountCommands Accounts { get; }
    internal static SkinCommands Skins { get; }
    internal static HostingCommands Hosting { get; }
    internal static AssistantCommands Assistant { get; }
    internal static AppCommands App { get; }

    internal static ParseResult Parse(IEnumerable<string> tokens)
    {
        var result = ParseUnchecked(tokens);
        Validate(result);
        return result;
    }

    internal static ParseResult ParseUnchecked(IEnumerable<string> tokens) => Root.Parse([.. tokens], ParserOptions);

    internal static void Validate(ParseResult result)
    {
        if (IsHelp(result))
            return;
        if (result.Errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        CliOptions.Validate(result);
        var positionalTokens = Path(result).SelectMany(command => command.Command.Arguments)
            .SelectMany(argument => result.GetResult(argument)?.Tokens ?? [])
            .ToHashSet<Token>(ReferenceEqualityComparer.Instance);
        foreach (var token in result.Tokens.TakeWhile(token => token.Type != TokenType.DoubleDash))
        {
            if (positionalTokens.Contains(token) && token.Value.StartsWith("--", StringComparison.Ordinal))
                throw new ArgumentException(string.Format(S.ErrorUnknownOption, token.Value));
        }

        if (result.CommandResult.Command is LauncherCommand command)
            command.ValidateInput?.Invoke(result);
    }

    internal static bool IsHelp(ParseResult result) => result.Action is HelpAction
        || (result.Errors.Count == 0 && (result.CommandResult.Command == Root || result.CommandResult.Command == Help));

    internal static Command HelpCommand(ParseResult result) => result.CommandResult.Command == Help
        ? Find(result.GetValue(A.HelpPath) ?? []) : result.CommandResult.Command;

    internal static Command Find(IEnumerable<string> path)
    {
        Command current = Root;
        foreach (var name in path)
        {
            current = current.Subcommands.FirstOrDefault(command => command.Name == name || command.Aliases.Contains(name))
                ?? throw new ArgumentException(string.Format(S.ErrorUnknownHelpTopic, name));
        }

        return current;
    }

    internal static IReadOnlyList<CommandResult> Path(ParseResult result)
    {
        var path = new List<CommandResult>();
        for (var command = result.CommandResult; command is not null && command.Command != Root; command = command.Parent as CommandResult)
            path.Add(command);
        path.Reverse();
        return path;
    }

    internal static string Operation(ParseResult result)
    {
        var path = Path(result);
        return string.Join('.', path.Select(command => command.IdentifierToken.Value)
            .Concat(path.SelectMany(command => command.Command.Arguments)
                .SelectMany(argument => result.GetResult(argument)?.Tokens.Select(token => token.Value) ?? []))
            .Take(2));
    }

    internal static string[] Tokens(ParseResult result, bool includeBackground = true)
    {
        var path = Path(result);
        var tokens = path.Select(command => command.Command.Name).ToList();
        foreach (var option in Root.Options.Concat(path.SelectMany(command => command.Command.Options)).Distinct())
        {
            if ((!includeBackground && option == O.Background) || result.GetResult(option) is not { Implicit: false } value)
                continue;
            if (value.Tokens.Count == 0)
                tokens.Add(option.Name + "=true");
            else
                tokens.AddRange(value.Tokens.Select(token => option.Name + "=" + token.Value));
        }

        tokens.Add("--");
        tokens.AddRange(path.SelectMany(command => command.Command.Arguments)
            .SelectMany(argument => result.GetResult(argument)?.Tokens.Select(token => token.Value) ?? []));
        return [.. tokens];
    }

    internal static IEnumerable<string> CommandPath(Command command)
    {
        var path = new Stack<string>();
        for (var current = command; current != Root; current = current.Parents.OfType<Command>().Single())
            path.Push(current.Name);
        return path;
    }

    internal static ParseResult FromCommand(Command command, IEnumerable<string>? arguments = null, IEnumerable<string>? options = null) =>
        Parse(CommandPath(command).Concat(options ?? []).Concat(["--"]).Concat(arguments ?? []));
    static LauncherCommands()
    {
        Root = new RootCommand(S.RootDescription);
        Root.Directives.Clear();
        foreach (var option in Root.Options.Where(option => option is VersionOption).ToArray())
            Root.Options.Remove(option);
        foreach (var option in new Option[] { O.Language, O.DataDirectory, O.Json, O.Events, O.Background, O.NonInteractive })
        {
            option.Recursive = true;
            Root.Options.Add(option);
        }

        Root.SetAction(result => ShowHelp(result.CommandResult.Command));
        Help = new Command("help", S.HelpDescription);
        Help.Arguments.Add(A.HelpPath);
        Help.SetAction(result => ShowHelp(HelpCommand(result)));
        Root.Subcommands.Add(Help);

        Information = new(Root);
        Instances = new(Root);
        Configuration = new(Root);
        Java = new(Root);
        Launch = new(Root);
        Sessions = new(Root);
        Logs = new(Root);
        Jobs = new(Root);
        Backups = new(Root);
        Loaders = new(Root);
        Runtime = new(Root);
        GameSettings = new(Root);
        ResourcePacks = new(Root);
        ShaderPacks = new(Root);
        Content = new(Root);
        Packs = new(Root);
        Worlds = new(Root);
        Screenshots = new(Root);
        Maintenance = new(Root);
        Servers = new(Root, Launch.Options);
        _ = new MultiplayerCommands(Root);
        Accounts = new(Root);
        Skins = new(Root);
        Hosting = new(Root);
        Assistant = new(Root);
        Repair = new(Assistant.Root);
        App = new(Root);
        CommandRegistration.Validate(Root);
    }

    internal static void ShowHelp(Command command) => WriteHelp(command, Console.Out);

    internal static void WriteHelp(Command command, TextWriter output) =>
        Root.Parse([.. CommandPath(command), "--help"], ParserOptions).Invoke(new InvocationConfiguration { Output = output, Error = output, EnableDefaultExceptionHandler = false });
}