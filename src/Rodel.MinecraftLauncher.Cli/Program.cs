// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.CommandLine;
using System.Globalization;
using System.Text.Json;
using A = Rodel.MinecraftLauncher.Cli.Strings.AssistantStrings;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal static class Program
{
    private static readonly Lock EventOutputLock = new();
    internal static Option<string?> LanguageOption { get; } = CreateLanguageOption();

    /// <summary>Configures startup language and UTF-8 output, then invokes the shared command tree with console cancellation.</summary>
    /// <param name="args">Process arguments as separate tokens, including global options and positional separators.</param>
    /// <returns>The command's exit code; caught failures return 1, invalid arguments 2, and cancellation 3, except detached hosts return 1.</returns>
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding = new System.Text.UTF8Encoding(false);
        Console.OutputEncoding = new System.Text.UTF8Encoding(false);
        ParseResult? parsed = null;
        using var cancellation = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            cancellation.Cancel();
        };
        try
        {
            ConfigureLanguage(args);
            parsed = LauncherCommands.ParseUnchecked(args);
            if (LauncherCommands.Path(parsed).Any(result => result.Command == LauncherCommands.Assistant.Root)
                && args.Any(value => value is "--api-key" or "--credential" or "--key"
                    || value.StartsWith("--api-key=", StringComparison.Ordinal) || value.StartsWith("--credential=", StringComparison.Ordinal) || value.StartsWith("--key=", StringComparison.Ordinal)))
            {
                throw new ArgumentException(A.SecretArgumentsForbidden);
            }

            LauncherCommands.Validate(parsed);
            return await parsed.InvokeAsync(new InvocationConfiguration
            {
                EnableDefaultExceptionHandler = false,
                ProcessTerminationTimeout = null
            }, cancellation.Token).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            var command = parsed?.CommandResult.Command;
            if (parsed is not null && (command == LauncherCommands.Hosting.SessionHost || command == LauncherCommands.Hosting.JobHost || command == LauncherCommands.Hosting.JobDispatch))
                return 1;
            var error = JsonSerializer.Serialize(ToolResult.Failure(parsed is null ? args.FirstOrDefault() : LauncherCommands.Operation(parsed), ex), CliJsonContext.Default.ToolResult);
            if (parsed is not null && LauncherCommands.Path(parsed).Any(result => result.Command == LauncherCommands.Hosting.Mcp))
                Console.Error.WriteLine(error);
            else
                Console.WriteLine(error);
            return ex is OperationCanceledException ? 3 : ex is ArgumentException ? 2 : 1;
        }
    }

    private static void ConfigureLanguage(string[] args)
    {
        var bootstrap = new RootCommand { TreatUnmatchedTokensAsErrors = false };
        bootstrap.Options.Clear();
        bootstrap.Directives.Clear();
        bootstrap.Options.Add(LanguageOption);
        var result = bootstrap.Parse(args, new ParserConfiguration
        {
            EnablePosixBundling = false,
            ResponseFileTokenReplacer = null
        });
        if (result.Errors.Count > 0)
            throw new ArgumentException(string.Join(Environment.NewLine, result.Errors.Select(error => error.Message)));
        var language = result.GetValue(LanguageOption) ?? Environment.GetEnvironmentVariable("RODEL_CLI_LANGUAGE");
        CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(LauncherLanguages.Resolve(string.IsNullOrWhiteSpace(language) ? CultureInfo.CurrentUICulture.Name : language));
        LanguageOption.Description = S.OptionLanguageDescription;
    }

    private static Option<string?> CreateLanguageOption()
    {
        var option = new Option<string?>("--language") { Recursive = true };
        option.AcceptOnlyFromAmong([.. LauncherLanguages.Supported]);
        return option;
    }

    private static LauncherApplication CreateApplication(ParseResult parsed, bool interactiveSecrets = false)
    {
        var paths = new LauncherPaths(parsed.GetValue(CliOptions.DataDirectory));
        return new LauncherApplication(paths, Environment.ProcessPath!, string.Equals(Path.GetFileNameWithoutExtension(Environment.ProcessPath), "dotnet", StringComparison.OrdinalIgnoreCase) ? [Path.Combine(AppContext.BaseDirectory, "rodel-mc.dll")] : [], parsed.GetValue(CliOptions.Events) ? data =>
        {
            lock (EventOutputLock)
                Console.Error.WriteLine(data.ToJsonString());
        }
        : null, allowInteractiveSecrets: interactiveSecrets);
    }

    internal static async Task<int> InvokeApplicationAsync(ParseResult parsed, CancellationToken ct)
    {
        var result = await CreateApplication(parsed, interactiveSecrets: true).ExecuteAsync(parsed, ct).ConfigureAwait(false);
        var envelope = CommandResults.FromCommand(LauncherCommands.Operation(parsed), result, parsed.CommandResult.Command as LauncherCommand);
        var json = new CliJsonContext(new JsonSerializerOptions(CliJsonContext.Default.Options)
        {
            WriteIndented = !parsed.GetValue(CliOptions.Json),
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
        Console.WriteLine(JsonSerializer.Serialize(envelope, json.ToolResult));
        return envelope.Status switch { LauncherResultStatus.Succeeded => 0, LauncherResultStatus.Canceled => 3, _ => 1 };
    }

    internal static async Task<int> RunSessionHostAsync(ParseResult parsed, CancellationToken _)
    {
        await new SessionService(new LauncherPaths(parsed.GetValue(CliOptions.DataDirectory))).RunMonitorAsync(parsed.GetRequiredValue(CliArguments.Target), CancellationToken.None).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RunJobHostAsync(ParseResult parsed, CancellationToken ct)
    {
        await CreateApplication(parsed).RunJobAsync(parsed.GetRequiredValue(CliArguments.Target), ct).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RunMcpAsync(ParseResult parsed, CancellationToken ct)
    {
        await McpHost.RunAsync(CreateApplication(parsed), parsed.GetValue(LauncherCommands.Hosting.ToolProfile)!, parsed.GetValue(LauncherCommands.Hosting.SkinScope), ct).ConfigureAwait(false);
        return 0;
    }

    internal static async Task<int> RunJobDispatchAsync(ParseResult parsed, CancellationToken _)
    {
        await CreateApplication(parsed).DispatchToolTaskAsync(parsed.GetRequiredValue(CliArguments.Target)).ConfigureAwait(false);
        return 0;
    }

    internal static Task<int> ListToolsAsync(ParseResult parsed, CancellationToken _)
    {
        var catalog = DiscoveryToolsCatalog.Select(parsed.GetValue(LauncherCommands.Hosting.ToolProfile)!, parsed.GetValue(LauncherCommands.Hosting.SkinScope));
        var tools = JsonSerializer.SerializeToNode([.. catalog.Select(tool => LauncherToolsCatalog.Description(tool))], CliJsonContext.Default.ToolDescriptionArray);
        var result = ToolResults.Success(LauncherCommands.Operation(parsed), tools);
        Console.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.ToolResult));
        return Task.FromResult(0);
    }

    internal static async Task<int> CallToolAsync(ParseResult parsed, CancellationToken ct)
    {
        var json = parsed.GetValue(CliOptions.ArgumentsFile) is { } file ? await File.ReadAllTextAsync(file, ct).ConfigureAwait(false) : parsed.GetValue(CliOptions.Arguments) ?? "{}";
        using var arguments = JsonDocument.Parse(json);
        var catalog = DiscoveryToolsCatalog.Select(parsed.GetValue(LauncherCommands.Hosting.ToolProfile)!, parsed.GetValue(LauncherCommands.Hosting.SkinScope));
        var result = await new LauncherToolDispatcher(CreateApplication(parsed), catalog, parsed.GetValue(LauncherCommands.Hosting.SkinScope)).InvokeAsync(parsed.GetRequiredValue(CliArguments.Target), arguments.RootElement, ct).ConfigureAwait(false);
        Console.WriteLine(JsonSerializer.Serialize(result, CliJsonContext.Default.ToolResult));
        return result.Status switch { LauncherResultStatus.Succeeded => 0, LauncherResultStatus.Canceled => 3, _ => 1 };
    }
}