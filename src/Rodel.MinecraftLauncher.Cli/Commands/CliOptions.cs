// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;

using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class CliOptions
{
    private static readonly List<Action<ParseResult>> ValueValidators = [];
    public static readonly Option<string?> Language = Program.LanguageOption;
    public static readonly Option<string?> DataDirectory = Text("data-dir", S.OptionDataDirectoryDescription);
    public static readonly Option<bool> Json = Flag("json", S.OptionJsonDescription);
    public static readonly Option<bool> Events = Flag("events", S.OptionEventsDescription);
    public static readonly Option<bool> Background = Flag("background", S.OptionBackgroundDescription);
    public static readonly Option<bool> NonInteractive = Flag("non-interactive", S.OptionNonInteractiveDescription);
    public static readonly Option<string?> Version = Text("version", S.OptionVersionDescription);
    public static readonly Option<string?> Java = Text("java", S.OptionJavaDescription);
    public static readonly Option<string?> Account = Text("account", S.OptionAccountDescription);
    public static readonly Option<string?> Player = Text("player", S.OptionPlayerDescription);
    public static readonly Option<int?> Memory = Integer("memory", S.OptionMemoryDescription, 1);
    public static readonly Option<string?> MemoryMode = Text("memory-mode", S.OptionMemoryModeDescription, "manual", "auto");
    public static readonly Option<string?> LogEncoding = Text("log-encoding", S.OptionLogEncodingDescription);
    public static readonly Option<string?> GameLanguage = Text("game-language", S.OptionGameLanguageDescription);
    public static readonly Option<bool?> LoggingCompatibility = NullableFlag("logging-compatibility", S.OptionLoggingCompatibilityDescription);
    public static readonly Option<int?> Width = Integer("width", S.OptionWidthDescription);
    public static readonly Option<int?> Height = Integer("height", S.OptionHeightDescription);
    public static readonly Option<bool?> Fullscreen = NullableFlag("fullscreen", S.OptionFullscreenDescription);
    public static readonly Option<string?> World = Text("world", S.OptionWorldDescription);
    public static readonly Option<string?> Server = Text("server", S.OptionServerDescription);
    public static readonly Option<string?> ServerAddress = Text("server-address", S.OptionServerAddressDescription);
    public static readonly Option<bool> NoServer = Flag("no-server", S.OptionNoServerDescription);
    public static readonly Option<bool> Dedicated = Flag("dedicated", S.OptionDedicatedDescription);
    public static readonly Option<string?> ServerOwner = Text("server-owner", S.OptionServerOwnerDescription);
    public static readonly Option<bool> Preferred = Flag("preferred", S.OptionPreferredDescription);
    public static readonly Option<string?> PreferredInstance = Text("preferred-instance", S.OptionPreferredInstanceDescription);
    public static readonly Option<string?> LoginMethod = Text("method", S.OptionLoginMethodDescription, "browser", "device-code");
    public static readonly Option<string[]> JvmArguments = Many("jvm-arg", S.OptionJvmArgumentsDescription);
    public static readonly Option<string[]> GameArguments = Many("game-arg", S.OptionGameArgumentsDescription);
    public static readonly Option<string[]> Environment = Many("env", S.OptionEnvironmentDescription);
    public static readonly Option<bool> Plan = Flag("plan", S.OptionPlanDescription);
    public static readonly Option<bool> Offline = Flag("offline", S.OptionOfflineDescription);
    public static readonly Option<bool> Wait = Flag("wait", S.OptionWaitDescription);
    public static readonly Option<bool> Force = Flag("force", S.OptionForceDescription);
    public static readonly Option<bool> Confirm = Flag("confirm", S.OptionConfirmDescription);
    public static readonly Option<bool> DeleteFiles = Flag("delete-files", S.OptionDeleteFilesDescription);
    public static readonly Option<bool> Stdio = Flag("stdio", S.OptionStdioDescription);
    public static readonly Option<string?> Instance = Text("instance", S.OptionInstanceDescription);
    public static readonly Option<string?> Directory = Text("directory", S.OptionDirectoryDescription);
    public static readonly Option<string?> GameDirectory = Text("game-directory", S.OptionGameDirectoryDescription);
    public static readonly Option<string?> MinecraftDirectory = Text("minecraft-directory", S.OptionMinecraftDirectoryDescription);
    public static readonly Option<string?> Profile = Text("profile", S.OptionProfileDescription);
    public static readonly Option<string?> LauncherConfig = Text("launcher-config", S.OptionLauncherConfigDescription);
    public static readonly Option<bool> Migrate = Flag("migrate", S.OptionMigrateDescription);
    public static readonly Option<bool> Copy = Flag("copy", S.OptionCopyDescription);
    public static readonly Option<string?> Architecture = Text("architecture", S.OptionArchitectureDescription);
    public static readonly Option<string?> Id = Text("id", S.OptionIdDescription);
    public static readonly Option<string?> Scope = Text("scope", S.OptionScopeDescription, "all", "worlds", "config");
    public static readonly Option<bool> IncludeDownloads = Flag("include-downloads", S.OptionIncludeDownloadsDescription);
    public static readonly Option<string?> Minecraft = Text("minecraft", S.OptionMinecraftDescription);
    public static readonly Option<string?> Loader = Text("loader", S.OptionLoaderDescription);
    public static readonly Option<string?> LoaderVersion = Text("loader-version", S.OptionLoaderVersionDescription);
    public static readonly Option<string[]> LibraryMirrors = Many("library-mirror", S.OptionLibraryMirrorsDescription);
    public static readonly Option<string?> Encoding = Text("encoding", S.OptionEncodingDescription);
    public static readonly Option<string?> ExpectedFingerprint = Text("expected-fingerprint", S.OptionExpectedFingerprintDescription);
    public static readonly Option<bool> Overwrite = Flag("overwrite", S.OptionOverwriteDescription);
    public static readonly Option<bool> Replace = Flag("replace", S.OptionReplaceDescription);
    public static readonly Option<bool> AllowIncompatible = Flag("allow-incompatible", S.OptionAllowIncompatibleDescription);
    public static readonly Option<string?> Platform = Text("platform", S.OptionPlatformDescription);
    public static readonly Option<string?> Type = Text("type", S.OptionTypeDescription, "all", "mod", "resourcepack", "shader", "modpack");
    public static readonly Option<int?> Offset = Integer("offset", S.OptionOffsetDescription, 0);
    public static readonly Option<int?> Limit = Integer("limit", S.OptionLimitDescription, 1, 100);
    public static readonly Option<string?> Index = Text("index", S.OptionIndexDescription, "relevance", "downloads", "follows", "newest", "updated");
    public static readonly Option<string?> Kind = Text("kind", S.OptionKindDescription);
    public static readonly Option<string?> ContentVersion = Text("content-version", S.OptionContentVersionDescription);
    public static readonly Option<string?> Algorithm = Text("algorithm", S.OptionAlgorithmDescription, "sha1", "sha512");
    public static readonly Option<bool> Attach = Flag("attach", S.OptionAttachDescription);
    public static readonly Option<string?> Channel = Text("channel", S.OptionChannelDescription, "release", "beta", "alpha");
    public static readonly Option<string[]> VersionSelections = Many("select-version", S.OptionVersionSelectionsDescription);
    public static readonly Option<string?> ExpectedLockFingerprint = Text("expected-lock-fingerprint", S.OptionExpectedLockFingerprintDescription);
    public static readonly Option<bool> NoDependencies = Flag("no-dependencies", S.OptionNoDependenciesDescription);
    public static readonly Option<string?> UpdateContext = Text("update-context", S.OptionUpdateContextDescription, "current", "source");
    public static readonly Option<string?> Review = Text("review", S.OptionReviewDescription);
    public static readonly Option<string?> Session = Text("session", S.OptionSessionDescription);
    public static readonly Option<string?> Name = Text("name", S.OptionNameDescription);
    public static readonly Option<string?> ManualDirectory = Text("manual-directory", S.OptionManualDirectoryDescription);
    public static readonly Option<string?> Project = Text("project", S.OptionProjectDescription);
    public static readonly Option<string?> PackVersion = Text("pack-version", S.OptionPackVersionDescription);
    public static readonly Option<string?> OptionalPolicy = Text("optional-policy", S.OptionOptionalPolicyDescription, "include-all", "exclude-all");
    public static readonly Option<string[]> IncludeOptional = Many("include-optional", S.OptionIncludeOptionalDescription);
    public static readonly Option<string[]> ExcludeOptional = Many("exclude-optional", S.OptionExcludeOptionalDescription);
    public static readonly Option<bool> ContentOnly = Flag("content-only", S.OptionContentOnlyDescription);
    public static readonly Option<string?> Format = Text("format", S.OptionFormatDescription, "mrpack", "curseforge");
    public static readonly Option<bool> IncludeLocalMods = Flag("include-local-mods", S.OptionIncludeLocalModsDescription);
    public static readonly Option<int?> Bytes = Integer("bytes", S.OptionBytesDescription, 8, 1048576);
    public static readonly Option<int?> Tail = Integer("tail", S.OptionTailDescription, 1);
    public static readonly Option<long?> StdoutOffset = Long("stdout-offset", S.OptionStdoutOffsetDescription);
    public static readonly Option<long?> StderrOffset = Long("stderr-offset", S.OptionStderrOffsetDescription);
    public static readonly Option<string?> State = Text("state", S.OptionStateDescription);
    public static readonly Option<string?> StateFile = Text("state-file", S.OptionStateFileDescription);
    public static readonly Option<string?> Level = Text("level", S.OptionLevelDescription);
    public static readonly Option<bool?> Redact = NullableFlag("redact", S.OptionRedactDescription);
    public static readonly Option<string?> Report = Text("report", S.OptionReportDescription);
    public static readonly Option<bool> Verify = Flag("verify", S.OptionVerifyDescription);
    public static readonly Option<int?> OlderThanDays = Integer("older-than-days", S.OptionOlderThanDaysDescription, 0);
    public static readonly Option<int?> Timeout = Integer("timeout", S.OptionTimeoutDescription, 1);
    public static readonly Option<string?> Tool = Text("tool", S.OptionToolDescription);
    public static readonly Option<string?> Topic = Text("topic", S.OptionTopicDescription);
    public static readonly Option<string?> Pointer = Text("pointer", S.OptionPointerDescription);
    public static readonly Option<int?> Characters = Integer("characters", S.OptionCharactersDescription, 256, 8000);
    public static readonly Option<int?> PropertyOffset = Integer("property-offset", S.OptionPropertyOffsetDescription, 0);
    public static readonly Option<string?> Arguments = Text("arguments", S.OptionArgumentsDescription);
    public static readonly Option<string?> ArgumentsFile = Text("arguments-file", S.OptionArgumentsFileDescription);

    private static Option<string?> Text(string name, string description, params string[] choices)
    {
        var option = new Option<string?>("--" + name) { Description = description };
        if (choices.Length > 0)
            option.AcceptOnlyFromAmong(choices);
        return option;
    }

    private static Option<bool> Flag(string name, string description) => new("--" + name) { Description = description };

    private static Option<bool?> NullableFlag(string name, string description) => new("--" + name)
    {
        Description = description,
        Arity = ArgumentArity.ZeroOrOne
    };

    private static Option<string[]> Many(string name, string description) => new("--" + name)
    {
        Description = description,
        Arity = ArgumentArity.OneOrMore,
        AllowMultipleArgumentsPerToken = false
    };

    private static Option<int?> Integer(string name, string description, int minimum = int.MinValue, int maximum = int.MaxValue)
    {
        var option = new Option<int?>("--" + name) { Description = description };
        ValueValidators.Add(result =>
        {
            if (result.GetValue(option) is { } value && (value < minimum || value > maximum))
                throw new ArgumentException(string.Format(S.ErrorOptionRange, name, minimum, maximum));
        });
        return option;
    }

    private static Option<long?> Long(string name, string description)
    {
        var option = new Option<long?>("--" + name) { Description = description };
        ValueValidators.Add(result =>
        {
            if (result.GetValue(option) is < 0)
                throw new ArgumentException(string.Format(S.ErrorOptionNonnegative, name));
        });
        return option;
    }

    internal static void Validate(ParseResult result)
    {
        foreach (var validate in ValueValidators)
            validate(result);
    }
}