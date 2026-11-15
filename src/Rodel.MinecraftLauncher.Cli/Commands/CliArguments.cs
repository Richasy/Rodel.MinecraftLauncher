// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class CliArguments
{
    public static readonly Argument<string> Instance = Text("instance", S.ArgumentInstanceDescription);
    public static readonly Argument<string> Target = Text("target", S.ArgumentTargetDescription);
    public static readonly Argument<string> Item = Text("item", S.ArgumentItemDescription);
    public static readonly Argument<string> Destination = Text("destination", S.ArgumentDestinationDescription);
    public static readonly Argument<string> Name = Text("name", S.ArgumentNameDescription);
    public static readonly Argument<string> Address = Text("address", S.ArgumentAddressDescription);
    public static readonly Argument<string?> OptionalAddress = new("address") { Description = S.ArgumentOptionalAddressDescription, Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string> Key = Text("key", S.ArgumentKeyDescription);
    public static readonly Argument<string?> OptionalKey = new("key") { Description = S.ArgumentOptionalKeyDescription, Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string> Value = Text("value", S.ArgumentValueDescription);
    public static readonly Argument<string> Changes = Text("changes", S.ArgumentChangesDescription);
    public static readonly Argument<string> OptionalTarget = new("target") { Description = S.ArgumentOptionalTargetDescription, Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string> LogSession = new("session") { Description = S.ArgumentLogSessionDescription, Arity = ArgumentArity.ZeroOrOne };
    public static readonly Argument<string[]> Items = new("items") { Description = S.ArgumentItemsDescription, Arity = ArgumentArity.OneOrMore };
    public static readonly Argument<string[]> Names = new("names") { Description = S.ArgumentNamesDescription, Arity = ArgumentArity.ZeroOrMore };
    public static readonly Argument<string[]> HelpPath = new("command") { Description = S.ArgumentHelpPathDescription, Arity = ArgumentArity.ZeroOrMore };

    private static Argument<string> Text(string name, string description) => new(name) { Description = description };
}