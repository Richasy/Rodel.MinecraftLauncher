// Copyright (c) Richasy. All rights reserved.
using Microsoft.Extensions.AI;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.Configuration;
using Rodel.MinecraftLauncher.Core.Runtime;
using System.Text.Json;
using System.Text.Json.Nodes;
using static Rodel.MinecraftLauncher.Core.Tools.ToolSerialization;
namespace Rodel.MinecraftLauncher.Core.Tools;
/// <summary>Executes transport-independent typed tools using the same Core services as the App and CLI.</summary>
internal sealed class LauncherToolService(LauncherClient client, Func<LauncherTaskRequest, string?, CancellationToken, Task<JobStatus>>? startTask = null)
{
    private readonly AsyncLocal<string?> _requestId = new();
    private readonly ToolResults _results = new(client.Paths.Root);
    internal LauncherClient Client => client;
    internal SkinToolActions Skins { get; } = new(client.Skins);
    /// <summary>Lists actual typed definitions without connecting to another executable.</summary>
    public static IReadOnlyList<AIFunctionDeclaration> Definitions => [.. ToolCatalog.Entries.Values.Select(entry => entry.Declaration)];
    /// <summary>Validates and invokes a registered Core tool, retaining bounded results for later reads.</summary>
    public async Task<JsonObject> InvokeAsync(string name, JsonObject arguments, CancellationToken ct, string? requestId = null)
    {
        var previousRequestId = _requestId.Value;
        _requestId.Value = requestId;
        ToolResult result;
        JsonObject? input = null;
        try
        {
            ct.ThrowIfCancellationRequested();
            var entry = ToolCatalog.Entries.GetValueOrDefault(name) ?? throw new LauncherOperationException("UNKNOWN_TOOL", "Unknown Core tool.");
            input = arguments.DeepClone().AsObject();
            var hasProperties = entry.Declaration.JsonSchema.TryGetProperty("properties", out var declared);
            var ignored = input.Select(pair => pair.Key).Where(key => !hasProperties || !declared.TryGetProperty(key, out _)).ToArray();
            foreach (var key in ignored) input.Remove(key);
            var data = await entry.Invoke(this, input, ct).ConfigureAwait(false);
            if (name is "instance_list" or "content_list" or "job_list" or "process_list") data = ToolResults.Page(data, input, name);
            if (name == "content_search" && data is JsonObject search) data = await _results.SummarizeSearchAsync(search, ct).ConfigureAwait(false);
            result = ToolResults.Success(name, data);
            if (ignored.Length > 0)
            {
                result.Warnings.Add((JsonNode)ProtocolJson.Object(new IgnoredArgumentsWarning("IGNORED_ARGUMENTS", ignored,
                "Undeclared arguments had no effect; inspect the typed schema.", ProtocolJson.Continue("capabilities", ProtocolJson.Object(new CapabilitiesInput(name), ToolInputJsonContext.Default.CapabilitiesInput))), ToolJsonContext.Default.IgnoredArgumentsWarning));
            }
        }
        catch (Exception error) { result = ToolResult.Failure(name, error); }
        finally { _requestId.Value = previousRequestId; }

        return ProtocolJson.Object(await _results.BoundAsync(result, input).ConfigureAwait(false), ToolJsonContext.Default.ToolResult);
    }
    private static Task<JsonNode?> Value<T>(T value) => Task.FromResult(Node(value));
    private static async Task<JsonNode?> ValueAsync<T>(Task<T> task) => Node(await task.ConfigureAwait(false));
    private string Game(string id) => client.Instances.Get(id).GameDirectory;
    private Task<JsonNode?> StartAsync(LauncherTaskRequest request, CancellationToken ct) => ValueAsync(startTask is null
        ? client.Tasks.StartAsync(request, _requestId.Value, ct)
        : startTask(request, _requestId.Value, ct));
    internal Task<JsonNode?> TaskAsync(LauncherTaskRequest request, CancellationToken ct) => StartAsync(request, ct);
    internal Task<JsonNode?> RepairShowAsync(AiRepairRead a, CancellationToken ct) => ValueAsync(client.Repairs.InspectAsync(a.RepairId, ct));
    internal Task<JsonNode?> RepairPlanAsync(AiRepairPlan a, CancellationToken ct) => ValueAsync(client.Repairs.PlanAsync(a, ct));
    internal Task<JsonNode?> RepairApplyAsync(AiRepairAction a, CancellationToken ct) => ValueAsync(client.Repairs.ApplyAsync(a, ct));
    internal Task<JsonNode?> RepairVerifyAsync(AiRepairAction a, CancellationToken ct) => ValueAsync(client.Repairs.VerifyAsync(a, ct));
    internal Task<JsonNode?> RepairRestoreAsync(AiRepairAction a, CancellationToken ct) => ValueAsync(client.Repairs.RestoreAsync(a, ct));
    internal Task<JsonNode?> AppListAsync(PageInput _, CancellationToken _1) => Value(client.AppControl.List());
    internal async Task<JsonNode?> AppShowAsync(AppInstanceInput a, CancellationToken ct) => await client.AppControl.InvokeAsync(a.AppId, new() { Action = AppControlAction.State }, ct).ConfigureAwait(false);
    internal async Task<JsonNode?> AppWallpapersAsync(AppWallpaperInput a, CancellationToken ct) => await client.AppControl.InvokeAsync(a.AppId, new() { Action = AppControlAction.WallpaperList, Theme = a.Theme }, ct).ConfigureAwait(false);
    internal async Task<JsonNode?> AppCacheAsync(AppInstanceInput a, CancellationToken ct) => await client.AppControl.InvokeAsync(a.AppId, new() { Action = AppControlAction.ImageCacheStatus }, ct).ConfigureAwait(false);
    internal async Task<JsonNode?> AppControlAsync(AppControlInput a, CancellationToken ct)
    {
        if (!Enum.IsDefined(a.Request.Action)) throw new ArgumentException("An App mutation action is required.");
        var request = JsonSerializer.SerializeToNode(a.Request, ToolInputJsonContext.Default.AppMutationRequest)!.Deserialize(AppControlJsonContext.Default.AppControlRequest)!;
        return await client.AppControl.InvokeAsync(a.AppId, request, ct).ConfigureAwait(false);
    }
    internal Task<JsonNode?> InstanceListAsync(PageInput _, CancellationToken _1) => Value(client.ListInstances());
    internal Task<JsonNode?> InstanceShowAsync(InstanceInput a, CancellationToken _) => Value(client.Instances.Get(a.InstanceId).Describe());
    internal async Task<JsonNode?> InstanceCreateAsync(InstanceCreateInput a, CancellationToken ct) => (await client.Instances.CreateAsync(a.Name, a.Version, ct).ConfigureAwait(false)).ToJson();
    internal Task<JsonNode?> RuntimeShowAsync(InstanceInput a, CancellationToken ct) => ValueAsync(client.Runtimes.ShowAsync(a.InstanceId, ct));
    internal Task<JsonNode?> RuntimeOperationsAsync(InstanceInput a, CancellationToken _) => Value(client.Runtimes.GetOperations(a.InstanceId));
    internal Task<JsonNode?> RuntimeOperationShowAsync(RuntimeOperationInput a, CancellationToken _) => Value(client.Runtimes.GetOperation(a.InstanceId, a.OperationId));
    internal Task<JsonNode?> RuntimeRollbackAsync(RuntimeOperationInput a, CancellationToken ct) => ValueAsync(client.Runtimes.RollbackAsync(a.InstanceId, a.OperationId, ct));
    internal Task<JsonNode?> RuntimeChangeAsync(RuntimeChangeInput a, CancellationToken ct)
    {
        RuntimeChangeOptions options = new() { MinecraftVersion = a.MinecraftVersion, VersionId = a.Version, Loader = a.Loader, LoaderVersion = a.LoaderVersion, JavaPath = a.JavaPath, RemoveLoader = a.Loader == "none", PlanOnly = a.Plan, Offline = a.Offline };
        return a.Background && !a.Plan ? StartAsync(new ChangeRuntimeTask(a.InstanceId, options), ct) : ValueAsync(client.Runtimes.ChangeAsync(a.InstanceId, options, ct));
    }
    internal Task<JsonNode?> ConfigGetAsync(ConfigGetInput a, CancellationToken _) => a.Key is null ? Value(client.Settings.GetEffective(a.InstanceId is null ? null : client.Instances.Get(a.InstanceId))) : Value(client.Settings.Get(a.Key, a.InstanceId is null ? null : client.Instances.Get(a.InstanceId)));
    internal async Task<JsonNode?> ConfigSetAsync(ConfigSetInput a, CancellationToken ct)
    {
        var instance = a.InstanceId is null ? null : client.Instances.Get(a.InstanceId);
        await client.Settings.SetAsync(a.Key, JsonNode.Parse(a.Value.GetRawText()), instance, ct).ConfigureAwait(false);
        return Node(client.Settings.Get(a.Key, instance));
    }
    internal async Task<JsonNode?> ConfigPatchAsync(ConfigPatchInput a, CancellationToken ct)
    {
        var instance = a.InstanceId is null ? null : client.Instances.Get(a.InstanceId);
        await client.Settings.UpdateAsync(a.Changes ?? [], a.RemoveKeys ?? [], instance, ct).ConfigureAwait(false);
        return Node(client.Settings.GetEffective(instance));
    }
    internal Task<JsonNode?> GameSettingsGetAsync(EncodedInstanceInput a, CancellationToken ct) => ValueAsync(client.GameSettings.GetAsync(Game(a.InstanceId), a.Encoding, ct));
    internal Task<JsonNode?> GameSettingsSetAsync(GameSettingsSetInput a, CancellationToken ct) => ValueAsync(client.GameSettings.SetAsync(Game(a.InstanceId), JsonSerializer.SerializeToNode(a.Changes, ToolInputJsonContext.Default.ScalarMap)!.AsObject(), a.Plan, a.ExpectedFingerprint, a.Encoding, ct));
    internal Task<JsonNode?> GameSettingsApplyAsync(GameSettingsApplyInput a, CancellationToken ct) => ValueAsync(client.GameSettings.ApplyPendingAsync(Game(a.InstanceId), a.Overwrite, ct));
    internal Task<JsonNode?> GameSettingsDiscardPendingAsync(InstanceInput a, CancellationToken ct) => ValueAsync(GameSettingsService.DiscardPendingAsync(Game(a.InstanceId), ct));
    internal Task<JsonNode?> ResourcepackListAsync(EncodedInstanceInput a, CancellationToken ct) => ValueAsync(client.GetResourcePacksAsync(a.InstanceId, a.Encoding, ct));
    internal Task<JsonNode?> ResourcepackSelectAsync(ResourcepackSelectInput a, CancellationToken ct) => ValueAsync(client.SelectResourcePacksAsync(a.InstanceId, new() { Names = a.Names, Replace = a.Replace, AllowIncompatible = a.AllowIncompatible, PlanOnly = a.Plan, ExpectedFingerprint = a.ExpectedFingerprint, Encoding = a.Encoding }, ct));
    internal Task<JsonNode?> ShaderpackListAsync(ShaderpackListInput a, CancellationToken ct) => ValueAsync(client.GetShaderPacksAsync(a.InstanceId, a.Engine, ct));
    internal Task<JsonNode?> ShaderpackSelectAsync(ShaderpackSelectInput a, CancellationToken ct) => ValueAsync(client.SelectShaderPackAsync(a.InstanceId, new() { FileName = a.FileName, Engine = a.Engine, PlanOnly = a.Plan, ExpectedFingerprint = a.ExpectedFingerprint }, ct));
    private static ContentWorkflowOptions ContentOptions(ContentInput a, bool plan = false) => new() { VersionId = a.VersionId, Platform = a.Platform ?? "modrinth", Kind = a.Kind ?? "mods", World = a.World, MinecraftVersion = a.MinecraftVersion, Loader = a.Loader, PlanOnly = plan, InstallOptions = new() { Channel = a.Channel, IncludeDependencies = a.IncludeDependencies, VersionSelections = a.VersionSelections ?? [] } };
    internal Task<JsonNode?> ContentSearchAsync(ContentSearchInput a, CancellationToken ct) => ValueAsync(client.ContentPlatform.SearchAsync(a.Platform ?? "modrinth", a.Query, a.ProjectType ?? "mod", a.Offset, a.Limit, a.MinecraftVersion, a.Loader, a.Index ?? "relevance", ct));
    internal Task<JsonNode?> ContentListAsync(ContentListInput a, CancellationToken ct) => ValueAsync(client.ListContentAsync(a.InstanceId, new() { Kind = a.Kind ?? "mods", World = a.World }, ct));
    internal Task<JsonNode?> ContentPlanAsync(ContentInput a, CancellationToken ct) => ValueAsync(client.InstallContentAsync(a.InstanceId, a.ProjectId, ContentOptions(a, true), ct));
    internal Task<JsonNode?> ContentInstallAsync(ContentInstallInput a, CancellationToken ct)
    {
        var options = ContentOptions(a) with { InstallOptions = ContentOptions(a).InstallOptions with { ExpectedLockFingerprint = a.ExpectedLockFingerprint } };
        return a.Background ? StartAsync(new InstallContentTask(a.InstanceId, a.ProjectId, options), ct) : ValueAsync(client.InstallContentAsync(a.InstanceId, a.ProjectId, options, ct));
    }
    internal Task<JsonNode?> ContentReviewInstallAsync(ContentReviewInstallInput a, CancellationToken ct) => ValueAsync(client.ReviewContentInstallAsync(a.InstanceId, a.ProjectId, ContentOptions(a), ct));
    internal Task<JsonNode?> ContentReviewUpdatesAsync(ContentReviewInput a, CancellationToken ct) => ValueAsync(client.ReviewContentUpdatesAsync(a.InstanceId, a.Files, new() { Kind = a.Kind ?? "mods", World = a.World, MinecraftVersion = a.MinecraftVersion, Loader = a.Loader, UseSourceContext = a.UseSourceContext, InstallOptions = new() { ExpectedLockFingerprint = a.ExpectedLockFingerprint, Channel = a.Channel, IncludeDependencies = a.IncludeDependencies, VersionSelections = a.VersionSelections ?? [], SourceSelections = a.SourceSelections } }, ct));
    internal Task<JsonNode?> ContentReviewShowAsync(ContentReviewShowInput a, CancellationToken ct) => ValueAsync(client.GetContentReviewAsync(a.InstanceId, a.ReviewId, ct));
    internal Task<JsonNode?> ContentApplyReviewAsync(ContentApplyInput a, CancellationToken ct)
    {
        if (!a.Confirm) throw new ArgumentException("Applying a frozen review requires confirmation.");
        return a.Background ? StartAsync(new ApplyContentReviewTask(a.InstanceId, a.ReviewId), ct) : ValueAsync(client.ApplyContentReviewAsync(a.InstanceId, a.ReviewId, ct));
    }
    internal Task<JsonNode?> JobListAsync(PageInput _, CancellationToken _1) => Value(client.Jobs.List());
    internal Task<JsonNode?> JobShowAsync(JobInput a, CancellationToken _) => Value(client.Jobs.Get(a.JobId));
    internal async Task<JsonNode?> JobCancelAsync(JobInput a, CancellationToken ct) => Node(await client.Jobs.CancelAsync(a.JobId, ct).ConfigureAwait(false));
    internal Task<JsonNode?> ProcessListAsync(PageInput _, CancellationToken _1) => Value(client.Sessions.List());
    internal Task<JsonNode?> ProcessStopAsync(ProcessStopInput a, CancellationToken _) => Value(client.Sessions.Stop(a.SessionId, a.Force));
    internal async Task<JsonNode?> ReadLogsAsync(ReadLogsInput a, CancellationToken ct) => a.Format == "events"
        ? Node(await client.LogEvents.ReadAsync(a.SessionId, a.Bytes, a.StdoutOffset, a.StderrOffset, a.ParserState, a.MinimumLevel, ct).ConfigureAwait(false))
        : Node(await client.Logs.ReadAsync(a.SessionId, a.Tail, a.Bytes, a.StdoutOffset, a.StderrOffset, ct).ConfigureAwait(false));
    internal async Task<JsonNode?> ResultReadAsync(ResultReadInput a, CancellationToken ct) => await _results.ReadResultAsync(ProtocolJson.Object(a, ToolInputJsonContext.Default.ResultReadInput), ct).ConfigureAwait(false);
    internal async Task<JsonNode?> ResultImageReadAsync(ResultImageReadInput a, CancellationToken ct)
    {
        var (Bytes, MimeType) = await ToolImages.ReadAsync(client.Paths.Root, a, ct).ConfigureAwait(false);
        return Node(new ToolImageInfo(a.ResultId, a.Pointer, MimeType, Bytes.Length));
    }
    internal async Task<JsonNode?> DiagnoseAsync(DiagnoseInput a, CancellationToken ct) => a.ReportPath is not null ? Node(await CrashReportService.AnalyzeAsync(file: a.ReportPath, ct: ct).ConfigureAwait(false)) : a.SessionId is not null ? Node(await client.Maintenance.DiagnoseSessionAsync(a.SessionId, ct).ConfigureAwait(false)) : Node(await client.Maintenance.DiagnoseAsync(a.InstanceId ?? throw new ArgumentException("Specify an instance or session."), a.Verify, ct).ConfigureAwait(false));
    private static LaunchOptions LaunchOptions(LaunchInput a, bool plan)
    {
        if (a.Environment?.Keys.Any(key => key.Contains('=')) == true) throw new ArgumentException("An environment variable name cannot contain '='.");
        return new() { JavaPath = a.JavaPath, VersionId = a.Version, PlayerName = a.Player, MaxMemoryMb = a.MemoryMb, Width = a.Width, Height = a.Height, Fullscreen = a.Fullscreen, World = a.World, Server = a.Server, AccountId = a.AccountId, ServerAddress = a.ServerAddress, InheritServer = !a.NoServer, GameArguments = a.GameArguments ?? [], Environment = a.Environment?.ToDictionary(p => p.Key, p => p.Value.ToArgument()) ?? [], MemoryMode = a.MemoryMode, LogEncoding = a.LogEncoding, GameLanguage = a.GameLanguage, LoggingCompatibility = a.LoggingCompatibility, PlanOnly = plan };
    }

    internal Task<JsonNode?> LaunchPlanAsync(LaunchInput a, CancellationToken ct) => ValueAsync(client.LaunchAsync(a.InstanceId, LaunchOptions(a, true), ct));
    internal Task<JsonNode?> LaunchAsync(LaunchStartInput a, CancellationToken ct)
    {
        var options = LaunchOptions(a, false) with { Offline = a.Offline, GameBridge = a.GameBridge };
        return a.Background ? StartAsync(new LaunchTask(a.InstanceId, options), ct) : ValueAsync(client.LaunchAsync(a.InstanceId, options, ct));
    }
    internal Task<JsonNode?> GameBridgeShowAsync(GameSessionInput a, CancellationToken ct) => ValueAsync(client.GameBridge.GetStatusAsync(a.SessionId, ct));
    internal Task<JsonNode?> GameSnapshotAsync(GameSessionInput a, CancellationToken ct) => ValueAsync(client.GameBridge.SnapshotAsync(a.SessionId, ct));
    internal Task<JsonNode?> GameQueryAsync(GameQueryInput a, CancellationToken ct) => ValueAsync(client.GameBridge.QueryAsync(a.SessionId, a.Request, ct));
    internal Task<JsonNode?> GameRegionAsync(GameRegionInput a, CancellationToken ct) => ValueAsync(client.GameBridge.ReadRegionAsync(a.SessionId, a.Request, ct));
    internal Task<JsonNode?> GameEditAsync(GameEditInput a, CancellationToken ct) => ValueAsync(client.GameBridge.EditAsync(a.SessionId, a.Request, ct));
    internal Task<JsonNode?> GameOperationShowAsync(GameOperationInput a, CancellationToken ct) => ValueAsync(client.GameBridge.GetOperationAsync(a.SessionId, a.OperationId, ct));
    internal Task<JsonNode?> GameOperationCancelAsync(GameOperationInput a, CancellationToken ct) => ValueAsync(client.GameBridge.CancelOperationAsync(a.SessionId, a.OperationId, ct));
    internal async Task<JsonNode?> ContextAsync(ContextInput a, CancellationToken ct)
    {
        if (a.Limit is < 1 or > 100) throw new ArgumentException("Use a context limit between 1 and 100.");
        var selected = a.InstanceId;
        var result = ProtocolJson.Object(new ToolContextHeader(DateTimeOffset.UtcNow.ToString("O"), selected, false), ToolJsonContext.Default.ToolContextHeader);
        async Task Section(string name, Func<Task<JsonNode?>> read, Func<JsonNode?, JsonNode?>? transform = null)
        {
            try
            {
                var value = await read().ConfigureAwait(false);
                result[name] = transform is null ? value : transform(value);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                result[name] = LauncherOperationException.ToResult(error);
            }
        }

        JsonObject Page(JsonNode? value, string tool) => ToolResults.Page(value, new JsonObject { ["limit"] = a.Limit, ["instanceId"] = selected }, tool);
        if (selected is null)
        {
            await Section("instances", () => InstanceListAsync(new(), ct), value => Page(value, "instance_list")).ConfigureAwait(false);
        }
        else
        {
            await Section("instance", () => InstanceShowAsync(new(selected), ct)).ConfigureAwait(false);
            await Section("settings", () => ConfigGetAsync(new(InstanceId: selected), ct)).ConfigureAwait(false);
            await Section("contentCompatibility", () => ValueAsync(client.GetContentContextAsync(selected, ct: ct))).ConfigureAwait(false);
            await Section("mods", () => ContentListAsync(new(selected), ct), value => Page(value, "content_list")).ConfigureAwait(false);
        }

        var game = result["instance"]?["gameDirectory"]?.GetValue<string>();
        JsonNode? Activities(JsonNode? value, string tool)
        {
            if (value is not JsonArray array) return value;
            if (selected is not null)
                array = new JsonArray([.. array.Where(item => item?["activityUnknown"]?.GetValue<bool>() == true || item?["status"]?.GetValue<string>() == "unreadable" || (game is not null && string.Equals(item?["gameDirectory"]?.GetValue<string>(), game, StringComparison.OrdinalIgnoreCase)) || item?["request"]?["parameters"]?["instanceId"]?.ToString() == selected).Select(item => item?.DeepClone())]);
            var ordered = new JsonArray([.. array.OrderByDescending(item => item?["createdAt"]?.GetValue<string>() ?? item?["startedAt"]?.GetValue<string>(), StringComparer.Ordinal).Select(item => item?.DeepClone())]);
            var page = ToolResults.Page(ordered, new JsonObject { ["limit"] = a.Limit }, tool);
            page["scope"] = selected is null ? "all" : "matching instance plus records with unknown activity; continuation lists all activity";
            if (selected is not null && page["continuation"] is JsonObject continuation)
                continuation["arguments"]!["offset"] = 0;
            return page;
        }

        await Section("jobs", () => JobListAsync(new(), ct), value => Activities(value, "job_list")).ConfigureAwait(false);
        await Section("sessions", () => ProcessListAsync(new(), ct), value => Activities(value, "process_list")).ConfigureAwait(false);
        result["nextSteps"] = new JsonArray("Use launch_plan or diagnose before troubleshooting a launch.", "Use content_search then content_plan, install selected versions, and verify with content_list.", "Discover exact tool schemas before further actions.");
        return result;
    }
}