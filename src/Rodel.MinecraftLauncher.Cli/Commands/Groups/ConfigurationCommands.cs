// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Configuration;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ConfigurationCommands
{
    internal LauncherCommand Get { get; }
    internal LauncherCommand Set { get; }
    internal LauncherCommand Patch { get; }
    internal Option<string?> Changes { get; } = new("--changes") { Description = S.ConfigurationPatchChanges };
    internal Option<string[]> RemoveKeys { get; } = new("--remove") { AllowMultipleArgumentsPerToken = false, Description = S.ConfigurationPatchRemove };

    internal ConfigurationCommands(Command root)
    {
        var config = Group(root, "config", S.GroupConfigDescription);
        Get = Add(config, "get", S.ConfigurationGetDescription, (app, result, ct) => Task.FromResult<JsonNode?>(Read(app, result)), [A.OptionalKey], [O.Instance]);
        Set = Add(config, "set", S.ConfigurationSetDescription, async (app, result, ct) => await WriteAsync(app, result, ct).ConfigureAwait(false), [A.Key, A.Value], [O.Instance]);
        Patch = Add(config, "patch", S.ConfigurationPatchDescription, async (app, result, ct) =>
            await PatchAsync(app, result, ct).ConfigureAwait(false), options: [Changes, RemoveKeys, O.Instance]);
    }

    private static JsonObject Read(LauncherApplication app, ParseResult args)
    {
        var settings = new SettingsService(app.Paths);
        var instance = args.GetValue(O.Instance) is { } id ? app.Instances.Get(id) : null;
        return args.GetValue(A.OptionalKey) is { } key
            ? ProtocolJson.Object(settings.Get(key, instance), ConfigurationJsonContext.Default.SettingValue)
            : ProtocolJson.Object(settings.GetEffective(instance), ConfigurationJsonContext.Default.EffectiveSettings);
    }

    private static async Task<JsonObject> WriteAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var settings = new SettingsService(app.Paths);
        var instance = args.GetValue(O.Instance) is { } id ? app.Instances.Get(id) : null;
        var raw = args.GetRequiredValue(A.Value);
        JsonNode? value;
        try
        {
            value = JsonNode.Parse(raw);
        }
        catch (JsonException)
        {
            value = JsonValue.Create(raw);
        }

        var selected = args.GetRequiredValue(A.Key);
        await settings.SetAsync(selected, value, instance, ct).ConfigureAwait(false);
        return ProtocolJson.Object(settings.Get(selected, instance), ConfigurationJsonContext.Default.SettingValue);
    }

    private async Task<JsonObject> PatchAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var text = args.GetValue(Changes);
        var parsed = text is null ? new JsonObject() : JsonNode.Parse(text) as JsonObject
            ?? throw new ArgumentException(S.ConfigurationPatchObjectRequired);
        var settings = new SettingsService(app.Paths);
        var instance = args.GetValue(O.Instance) is { } id ? app.Instances.Get(id) : null;
        await settings.UpdateAsync(parsed, args.GetValue(RemoveKeys) ?? [], instance, ct).ConfigureAwait(false);
        return ProtocolJson.Object(settings.GetEffective(instance), ConfigurationJsonContext.Default.EffectiveSettings);
    }
}