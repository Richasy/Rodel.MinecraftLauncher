// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Screenshots;
using System.CommandLine;
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class ScreenshotCommands
{
    internal ScreenshotCommands(Command root)
    {
        var screenshots = Group(root, "screenshot", S.ScreenshotGroupDescription);
        var file = new Argument<string>("file") { Description = S.ScreenshotFileDescription };
        var files = new Argument<string[]>("files") { Description = S.ScreenshotFilesDescription, Arity = ArgumentArity.OneOrMore };
        var optionalFile = new Argument<string?>("file") { Description = S.ScreenshotOptionalFileDescription, Arity = ArgumentArity.ZeroOrOne };
        var permanent = new Option<bool>("--permanent") { Description = S.ScreenshotPermanentDescription };
        Add(screenshots, "list", S.ScreenshotListDescription,
            (app, args, ct) => app.Client.Screenshots.ListAsync(args.GetRequiredValue(A.Instance), ct),
            ScreenshotJsonContext.Default.ScreenshotCatalog, [A.Instance]);
        Add(screenshots, "show", S.ScreenshotShowDescription,
            (app, args, ct) => Task.FromResult(app.Client.Screenshots.GetFile(args.GetRequiredValue(A.Instance), args.GetRequiredValue(file))),
            ScreenshotJsonContext.Default.ScreenshotFile, [A.Instance, file]);
        Add(screenshots, "open", S.ScreenshotOpenDescription, (app, args, ct) =>
        {
            ct.ThrowIfCancellationRequested();
            var service = app.Client.Screenshots;
            var instanceId = args.GetRequiredValue(A.Instance);
            var path = args.GetValue(optionalFile) is { } name ? service.GetFile(instanceId, name).Path : service.EnsureDirectory(instanceId);
            using var process = Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
            return Task.FromResult<JsonNode?>(JsonValue.Create(path));
        }, [A.Instance, optionalFile]);
        Add(screenshots, "rename", S.ScreenshotRenameDescription, (app, args, ct) =>
        {
            var service = app.Client.Screenshots;
            var instanceId = args.GetRequiredValue(A.Instance);
            return service.RenameAsync(instanceId, service.GetFile(instanceId, args.GetRequiredValue(file)), args.GetRequiredValue(A.Name), ct);
        }, ScreenshotJsonContext.Default.ScreenshotFile, [A.Instance, file, A.Name]);
        Add(screenshots, "export", S.ScreenshotExportDescription, async (app, args, ct) =>
        {
            var service = app.Client.Screenshots;
            var instanceId = args.GetRequiredValue(A.Instance);
            var selected = args.GetRequiredValue(files).Select(name => service.GetFile(instanceId, name)).ToArray();
            return RequireCompleted(await service.ExportAsync(instanceId, selected, args.GetRequiredValue(A.Destination), ct).ConfigureAwait(false), ct);
        }, ScreenshotJsonContext.Default.ScreenshotBatchResult, [A.Instance, A.Destination, files]);
        Confirm(Add(screenshots, "remove", S.ScreenshotRemoveDescription, async (app, args, ct) =>
        {
            var service = app.Client.Screenshots;
            var instanceId = args.GetRequiredValue(A.Instance);
            var selected = args.GetRequiredValue(files).Select(name => service.GetFile(instanceId, name)).ToArray();
            var result = args.GetValue(permanent)
                ? await service.DeleteAsync(instanceId, selected, ct).ConfigureAwait(false)
                : await service.RecycleAsync(instanceId, selected, ct).ConfigureAwait(false);
            return RequireCompleted(result, ct);
        }, ScreenshotJsonContext.Default.ScreenshotBatchResult, [A.Instance, files], [O.Confirm, permanent]));
    }

    private static ScreenshotBatchResult RequireCompleted(ScreenshotBatchResult result, CancellationToken ct)
    {
        if (result.Succeeded) return result;
        var details = JsonSerializer.SerializeToNode(result, ScreenshotJsonContext.Default.ScreenshotBatchResult)!;
        if (result.Canceled)
            throw new LauncherOperationCanceledException(S.ScreenshotCanceled, details, new OperationCanceledException(ct));
        throw new LauncherOperationException("SCREENSHOT_PARTIAL_FAILURE", S.ScreenshotPartialFailure, details);
    }
}