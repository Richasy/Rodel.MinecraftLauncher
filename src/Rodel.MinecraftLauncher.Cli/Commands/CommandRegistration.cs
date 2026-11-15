// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class CommandRegistration
{
    internal static void Validate(Command root) => Validate(root, []);

    private static void Validate(Command root, IReadOnlyList<Option> inherited)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in root.Subcommands)
        {
            foreach (var name in command.Aliases.Prepend(command.Name))
            {
                if (!names.Add(name))
                    throw new InvalidOperationException(string.Format(S.ErrorDuplicateCommand, name, root.Name));
            }
        }

        names.Clear();
        var options = inherited.Concat(root.Options).Distinct().ToArray();
        foreach (var option in options)
        {
            foreach (var name in option.Aliases.Prepend(option.Name))
            {
                if (!names.Add(name))
                    throw new InvalidOperationException(string.Format(S.ErrorDuplicateOption, name, root.Name));
            }
        }

        var recursive = options.Where(option => option.Recursive).ToArray();
        foreach (var command in root.Subcommands)
            Validate(command, recursive);
    }
}