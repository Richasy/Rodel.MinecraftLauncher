// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using System.Globalization;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class CliInvocation(Command command, params string[] arguments)
{
    private readonly List<string> _arguments = [.. arguments];
    private readonly List<string> _options = [];

    /// <summary>Adds positional values as separate tokens without shell parsing or whitespace splitting.</summary>
    /// <param name="values">Literal values to append after the command's options and separator.</param>
    /// <returns>This invocation so that further arguments or options can be added.</returns>
    public CliInvocation Pos(params string[] values)
    {
        _arguments.AddRange(values);
        return this;
    }

    /// <summary>Adds a declared scalar option using invariant formatting; null leaves the option unspecified.</summary>
    /// <typeparam name="T">The value type declared by the actual command option.</typeparam>
    /// <param name="option">An option belonging to the command or a recursive ancestor.</param>
    /// <param name="value">A supported scalar value; false and empty strings remain explicit values.</param>
    /// <returns>This invocation with the option appended when a value is present.</returns>
    public CliInvocation Option<T>(Option<T> option, T value)
    {
        CheckOption(option);
        if (value is not null)
        {
            var text = value switch
            {
                string literal => literal,
                bool flag => flag ? "true" : "false",
                IFormattable number => number.ToString(null, CultureInfo.InvariantCulture),
                _ => throw new ArgumentException(string.Format(S.ErrorOptionScalar, option.Name))
            };
            _options.Add(option.Name + "=" + text);
        }

        return this;
    }

    /// <summary>Adds each value as an occurrence of a declared repeatable option, preserving token boundaries.</summary>
    /// <param name="option">The repeatable option owned by this command or a recursive ancestor.</param>
    /// <param name="values">Values to append; null adds no occurrences.</param>
    /// <returns>This invocation with the supplied option occurrences appended.</returns>
    public CliInvocation Values(Option<string[]> option, IEnumerable<string>? values)
    {
        CheckOption(option);
        foreach (var value in values ?? [])
            _options.Add(option.Name + "=" + value);
        return this;
    }

    /// <summary>Parses and validates the invocation through the shared command tree without executing it.</summary>
    /// <returns>The command's typed parse result, or an exception when validation fails.</returns>
    public ParseResult Parse() => LauncherCommands.FromCommand(command, _arguments, _options);

    private void CheckOption(Option option)
    {
        for (var owner = command; owner is not null; owner = owner.Parents.OfType<Command>().SingleOrDefault())
        {
            if (owner.Options.Any(declared => declared == option && (owner == command || declared.Recursive)))
                return;
        }

        throw new InvalidOperationException(string.Format(S.ErrorOptionNotDeclared, option.Name, string.Join(' ', LauncherCommands.CommandPath(command))));
    }
}