// Copyright (c) Richasy. All rights reserved.

using Rodel.Minecraft.Seeds;
using Rodel.MinecraftLauncher.Core.Application;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using O = Rodel.MinecraftLauncher.Cli.CliOptions;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

internal sealed class SeedExplorerCommands
{
    private readonly Option<string?> _request = new("--request") { Description = S.SeedRequestDescription };
    private readonly Option<string?> _requestFile = new("--request-file") { Description = S.SeedRequestFileDescription };
    private readonly Option<string> _gameLanguage = new("--game-language") { Description = S.SeedGameLanguageDescription, DefaultValueFactory = _ => "en_us" };
    private readonly Option<string> _output = new("--output") { Description = S.SeedOutputDescription, Required = true };

    internal SeedExplorerCommands(Command world)
    {
        var seed = Group(world, "seed", S.SeedGroupDescription);
        Add(seed, "versions", S.SeedVersionsDescription, (app, _, ct) => app.Client.Seeds.ListVersionsAsync(ct), SeedJsonContext.Default.SeedVersionOptionArray);
        var prepare = Add(seed, "prepare", S.SeedPrepareDescription, (app, result, ct) => app.Client.PrepareSeedRuntimeAsync(result.GetValue(O.Version) ?? "26.3", ct),
            SeedJsonContext.Default.SeedRuntimeProfile, options: [O.Version]);
        prepare.TaskFactory = result => new PrepareSeedRuntimeTask(result.GetValue(O.Version) ?? "26.3");
        var catalog = Add(seed, "catalog", S.SeedCatalogDescription,
            (app, result, ct) => app.Client.Seeds.GetCatalogAsync(Profile(result), result.GetRequiredValue(_gameLanguage), ct),
            SeedJsonContext.Default.SeedCatalog, options: [O.MinecraftDirectory, O.Version, O.Java, _gameLanguage]);
        Required(catalog, O.MinecraftDirectory);
        Required(catalog, O.Version);
        WithRequest(Add(seed, "spawn", S.SeedSpawnDescription, (app, result, ct) =>
            app.Client.Seeds.SpawnAsync(Read(result, SeedJsonContext.Default.SeedSpawnQuery), ct), SeedJsonContext.Default.SeedSpawnResult,
            options: [_request, _requestFile]));
        var map = WithRequest(Add(seed, "map", S.SeedMapDescription, (app, result, ct) =>
            app.Client.Seeds.MapAsync(Read(result, SeedJsonContext.Default.SeedMapQuery), ct), SeedJsonContext.Default.SeedMapTile, options: [_request, _requestFile]));
        map.TaskFactory = result => new MapSeedTask(Read(result, SeedJsonContext.Default.SeedMapQuery));
        WithRequest(Add(seed, "structures", S.SeedStructuresDescription, (app, result, ct) =>
            app.Client.Seeds.FindStructuresAsync(Read(result, SeedJsonContext.Default.SeedStructureQuery), ct), SeedJsonContext.Default.SeedStructureResult,
            options: [_request, _requestFile]));
        WithRequest(Add(seed, "region", S.SeedRegionDescription, (app, result, ct) =>
            app.Client.Seeds.RegionAsync(Read(result, SeedJsonContext.Default.SeedRegionQuery), cancellationToken: ct), SeedJsonContext.Default.SeedRegionResult,
            options: [_request, _requestFile]));
        WithRequest(Add(seed, "export", S.SeedExportDescription, async (app, result, ct) =>
        {
            var tile = await app.Client.Seeds.MapAsync(Read(result, SeedJsonContext.Default.SeedMapQuery), ct).ConfigureAwait(false);
            return await SeedExplorerService.ExportAsync(tile, result.GetRequiredValue(_output), cancellationToken: ct).ConfigureAwait(false);
        }, SeedStoreJsonContext.Default.SeedMapExport, options: [_request, _requestFile, _output]));

        var search = Group(seed, "search", S.SeedSearchDescription);
        var start = WithRequest(Add(search, "start", S.SeedSearchStartDescription, async (app, result, ct) =>
        {
            var state = await app.Client.Seeds.Searches.CreateAsync(Read(result, SeedJsonContext.Default.SeedSearchQuery), ct).ConfigureAwait(false);
            return await app.Client.SearchSeedsAsync(state.Id, ct).ConfigureAwait(false);
        }, SeedStoreJsonContext.Default.SeedSearchState, options: [_request, _requestFile]));
        start.AsyncTaskFactory = async (client, result, ct) => new SearchSeedsTask(
            (await client.Seeds.Searches.CreateAsync(Read(result, SeedJsonContext.Default.SeedSearchQuery), ct).ConfigureAwait(false)).Id);
        Add(search, "list", S.SeedSearchListDescription, (app, _, _) => Task.FromResult(app.Client.Seeds.Searches.List()), SeedStoreJsonContext.Default.SeedSearchStateArray);
        Add(search, "show", S.SeedSearchShowDescription, (app, result, _) => Task.FromResult(app.Client.Seeds.Searches.Read(result.GetRequiredValue(A.Item))),
            SeedStoreJsonContext.Default.SeedSearchState, [A.Item]);
        var resume = Add(search, "resume", S.SeedSearchResumeDescription, (app, result, ct) => app.Client.SearchSeedsAsync(result.GetRequiredValue(A.Item), ct),
            SeedStoreJsonContext.Default.SeedSearchState, [A.Item]);
        resume.TaskFactory = result => new SearchSeedsTask(result.GetRequiredValue(A.Item));

        var bookmark = Group(seed, "bookmark", S.SeedBookmarkDescription);
        Add(bookmark, "list", S.SeedBookmarkListDescription, (app, _, _) => Task.FromResult(app.Client.Seeds.Bookmarks.List()), SeedStoreJsonContext.Default.SeedBookmarkArray);
        WithRequest(Add(bookmark, "save", S.SeedBookmarkSaveDescription, (app, result, ct) =>
            app.Client.Seeds.Bookmarks.SaveAsync(Read(result, SeedStoreJsonContext.Default.SeedBookmark), ct), SeedStoreJsonContext.Default.SeedBookmark, options: [_request, _requestFile]));
        Add(bookmark, "remove", S.SeedBookmarkRemoveDescription, async (app, result, ct) =>
        {
            await app.Client.Seeds.Bookmarks.RemoveAsync(result.GetRequiredValue(A.Item), ct).ConfigureAwait(false);
            return JsonSerializer.SerializeToNode(app.Client.Seeds.Bookmarks.List(), SeedStoreJsonContext.Default.SeedBookmarkArray);
        }, [A.Item]);
    }

    private LauncherCommand WithRequest(LauncherCommand command)
    {
        command.Validators.Add(result =>
        {
            if ((result.GetValue(_request) is null) == (result.GetValue(_requestFile) is null)) result.AddError(S.SeedRequestChoiceError);
        });
        return command;
    }

    private T Read<T>(ParseResult result, JsonTypeInfo<T> metadata)
    {
        var json = result.GetValue(_request) ?? File.ReadAllText(result.GetRequiredValue(_requestFile)!);
        try { return JsonSerializer.Deserialize(json, metadata) ?? throw new ArgumentException(S.SeedRequestEmptyError); }
        catch (JsonException exception) { throw new ArgumentException(string.Format(S.SeedRequestInvalidError, exception.Message), exception); }
    }

    private static SeedRuntimeProfile Profile(ParseResult result) => new()
    {
        MinecraftDirectory = result.GetValue(O.MinecraftDirectory)!,
        VersionId = result.GetValue(O.Version)!,
        JavaPath = result.GetValue(O.Java),
    };
}