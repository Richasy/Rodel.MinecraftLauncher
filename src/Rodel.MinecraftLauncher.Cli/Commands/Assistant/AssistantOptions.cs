// Copyright (c) Richasy. All rights reserved.

using System.CommandLine;
using C = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;
using S = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;

namespace Rodel.MinecraftLauncher.Cli.Assistant;

internal static class AssistantOptions
{
    public static readonly Option<string?> DataDirectory = CliOptions.DataDirectory;
    public static readonly Option<bool> Json = CliOptions.Json;
    public static readonly Option<bool> Events = CliOptions.Events;
    public static readonly Option<string?> File = Text("file");
    public static readonly Option<string?> Id = Text("id");
    /// <summary>Names an already player-authorized incident rather than granting general operation access.</summary>
    public static readonly Option<string?> Repair = new("--repair") { Description = C.RepairGroup };
    public static readonly Option<string?> Name = Text("name");
    public static readonly Option<string?> Protocol = Text("protocol");
    public static readonly Option<string?> BaseUrl = Text("base-url");
    public static readonly Option<string[]> Models = new("--model") { AllowMultipleArgumentsPerToken = false, Description = S.ModelSelection };
    public static readonly Option<string?> Service = Text("service");
    public static readonly Option<string?> CredentialEnvironment = Text("credential-env");
    public static readonly Option<bool> CredentialStdin = Flag("credential-stdin");
    public static readonly Option<bool> CredentialPrompt = Flag("credential-prompt");
    public static readonly Option<bool> ClearCredential = Flag("clear-credential");
    public static readonly Option<bool?> Enabled = Boolean("enabled");
    public static readonly Option<bool?> AllowHttp = Boolean("allow-http");
    public static readonly Option<long?> Revision = new("--expected-revision");
    public static readonly Option<double?> Temperature = new("--temperature");
    public static readonly Option<int?> OutputTokens = new("--max-output-tokens") { Description = S.OutputTokens };
    public static readonly Option<int?> ContextLength = new("--context-length") { Description = C.LocalContextLengthDescription };
    public static readonly Option<bool> ClearContextLength = new("--clear-context-length") { Description = C.ClearLocalContextDescription };
    public static readonly Option<bool> ClearParameters = Flag("clear-parameters");
    public static readonly Option<string?> Prompt = Text("prompt");
    public static readonly Option<string?> PromptFile = Text("prompt-file");
    public static readonly Option<string[]> Images = new("--image") { AllowMultipleArgumentsPerToken = false, Description = C.AiReferenceImages };
    public static readonly Option<bool> PromptStdin = new("--prompt-stdin") { Description = S.PromptStdin };
    public static readonly Option<int?> Timeout = new("--timeout-seconds") { Description = S.TimeoutSeconds };
    public static readonly Option<string?> Title = Text("title");
    public static readonly Option<string?> Instance = Text("instance");
    public static readonly Option<bool> ClearInstance = Flag("clear-instance");
    public static readonly Option<bool> GenerateTitle = Flag("generate-title");
    public static readonly Option<string?> ClientKey = new("--client-key") { Description = S.ClientKey };
    public static readonly Option<bool> NoTools = Flag("no-tools");
    public static readonly Option<bool> AllowOperations = new("--allow-operations") { Description = S.AllowOperations };
    public static readonly Option<bool?> Approve = new("--approve") { Arity = ArgumentArity.ExactlyOne, Description = S.Approve };
    public static readonly Option<bool> Wait = Flag("wait");
    public static readonly Option<bool> CancelOnExit = Flag("cancel-on-exit");
    public static readonly Option<int?> ModelCalls = new("--max-turns") { Description = S.ModelCalls };
    public static readonly Option<int> Offset = new("--offset");
    public static readonly Option<int?> Limit = new("--limit");
    public static readonly Option<int?> Characters = new("--characters") { Description = S.ArchiveCharacters };
    public static readonly Option<int?> MessageIndex = new("--message-index") { Description = S.ArchiveMessageIndex };
    public static readonly Option<long> After = new("--after");
    public static readonly Option<string?> Mode = Text("mode");
    public static readonly Option<string?> ApprovalScope = Text("approval-scope");
    public static readonly Option<bool?> Files = Boolean("files");
    public static readonly Option<bool?> Network = Boolean("network");
    public static readonly Option<string?> Output = Text("output");
    public static readonly Option<string?> Format = Text("format");
    public static readonly Option<bool> Overwrite = Flag("overwrite");
    public static readonly Option<bool> Confirm = Flag("confirm");
    public static readonly Option<string?> Fingerprint = Text("fingerprint");
    public static readonly Option<string?> Path = Text("path");
    public static readonly Argument<string> Target = new("target");
    public static readonly Argument<string> Conversation = new("conversation");
    public static readonly Argument<string> Turn = new("turn");
    public static readonly Argument<string> Archive = new("archive");
    public static readonly Argument<string> Invocation = new("invocation");
    public static readonly Argument<string?> OptionalTarget = new("target") { Arity = ArgumentArity.ZeroOrOne };
    private static Option<string?> Text(string name) => new("--" + name) { Description = string.Format(S.OptionValue, name) };
    private static Option<bool> Flag(string name) => new("--" + name) { Description = string.Format(S.OptionFlag, name) };
    private static Option<bool?> Boolean(string name) => new("--" + name) { Arity = ArgumentArity.ExactlyOne, Description = string.Format(S.OptionBoolean, name) };
}