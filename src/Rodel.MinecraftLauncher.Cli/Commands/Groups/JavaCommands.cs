// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Java;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class JavaCommands
{

    internal JavaCommands(Command root)
    {
        var java = Group(root, "java", S.GroupJavaDescription);
        Add(java, "list", S.JavaListDescription, async (app, result, ct) => await DiscoverAsync(app, result, ct).ConfigureAwait(false), options: [O.Java]);
        Add(java, "available", S.JavaAvailableDescription, async (app, result, ct) => ProtocolJson.Object(await new JavaRuntimeService(app.Paths).GetAvailableAsync(result.GetValue(O.Offline), ct).ConfigureAwait(false), CliJsonContext.Default.JavaRuntimeCatalog), options: [O.Offline]);
        Add(java, "managed", S.JavaManagedDescription, async (app, result, ct) => JsonSerializer.SerializeToNode(await new JavaRuntimeService(app.Paths).ListAsync(ct).ConfigureAwait(false), CliJsonContext.Default.IReadOnlyListJavaRuntimeSummary));
        Add(java, "install", S.JavaInstallDescription, async (app, result, ct) => await new JavaRuntimeService(app.Paths).InstallAsync(component: result.GetRequiredValue(A.Target), architecture: result.GetValue(O.Architecture), progress: app.DownloadEvents, ct: ct).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRegistrationResult, [A.Target], [O.Architecture])
            .TaskFactory = result => new InstallJavaTask(result.GetRequiredValue(A.Target), result.GetValue(O.Architecture));
        Add(java, "import", S.JavaImportDescription, async (app, result, ct) => await new JavaRuntimeService(app.Paths).ImportAsync(result.GetRequiredValue(A.Target), result.GetValue(O.Id), ct).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRegistrationResult, [A.Target], [O.Id]);
        Add(java, "remove", S.JavaRemoveDescription, async (app, result, ct) => await new JavaRuntimeService(app.Paths).RemoveAsync(result.GetRequiredValue(A.Target), ct).ConfigureAwait(false), JavaOperationJsonContext.Default.JavaRemovalResult, [A.Target]);
        Add(java, "select", S.JavaSelectDescription, async (app, result, ct) => await SelectAsync(app, result, ct).ConfigureAwait(false), [A.Target], [O.Instance]);
    }

    private static async Task<JsonArray> DiscoverAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var java = await new JavaDiscovery(app.Paths).DiscoverAsync(args.GetValue(O.Java), ct: ct).ConfigureAwait(false);
        return [.. java.Select(runtime => (JsonNode)new JsonObject
        {
            ["path"] = runtime.Path,
            ["majorVersion"] = runtime.MajorVersion,
            ["version"] = runtime.FullVersion,
            ["javaHome"] = runtime.JavaHome,
            ["discoveryPath"] = runtime.DiscoveryPath,
            ["architecture"] = runtime.Architecture,
            ["bitness"] = runtime.Bitness,
            ["fileEncoding"] = runtime.FileEncoding,
            ["nativeEncoding"] = runtime.NativeEncoding,
            ["usable"] = runtime.IsUsable,
            ["warnings"] = new JsonArray([.. runtime.Warnings.Select(warning => (JsonNode?)JsonValue.Create(warning))]),
            ["output"] = runtime.VersionOutput
        })];
    }

    private static async Task<JsonObject> SelectAsync(LauncherApplication app, ParseResult args, CancellationToken ct)
    {
        var instance = args.GetValue(O.Instance) is { } id ? app.Instances.Get(id) : null;
        var settings = new SettingsService(app.Paths);
        await settings.SetAsync("javaPath", JsonValue.Create(args.GetRequiredValue(A.Target)), instance, ct).ConfigureAwait(false);
        return ProtocolJson.Object(settings.Get("javaPath", instance), ConfigurationJsonContext.Default.SettingValue);
    }
}