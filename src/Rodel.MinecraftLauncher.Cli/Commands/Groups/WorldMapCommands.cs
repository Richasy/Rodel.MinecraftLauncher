// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Worlds.Maps;
using Rodel.MinecraftLauncher.Core.Worlds.Seeds;
using System.CommandLine;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using static Rodel.MinecraftLauncher.Cli.CommandFactory;
using A = Rodel.MinecraftLauncher.Cli.CliArguments;
using S = Rodel.MinecraftLauncher.Cli.Strings.CliStrings;

namespace Rodel.MinecraftLauncher.Cli;

/// <summary>Maps saved-world inspection directly to Core; generation remains under world seed.</summary>
internal sealed class WorldMapCommands
{
    private readonly Option<string?> _request = new("--request") { Description = S.WorldMapRequestDescription };
    private readonly Option<string?> _requestFile = new("--request-file") { Description = S.WorldMapRequestFileDescription };
    private readonly Option<string?> _playerId = new("--player-id") { Description = S.WorldMapPlayerIdDescription };
    private readonly Option<string> _output = new("--output") { Description = S.SeedOutputDescription, Required = true };
    private readonly Option<bool> _fillUnexplored = new("--fill-unexplored") { Description = S.WorldMapFillUnexploredDescription };

    internal WorldMapCommands(Command world)
    {
        var map = Group(world, "map", S.WorldMapGroupDescription);
        Add(map, "show", S.WorldMapShowDescription, (app, result, ct) => app.Client.WorldMaps.DescribeAsync(Selection(result), ct),
            WorldMapJsonContext.Default.WorldMapSnapshot, [A.Instance, A.Item]);
        Add(map, "player", S.WorldMapPlayerDescription, (app, result, ct) => app.Client.WorldMaps.ObservePlayerAsync(Selection(result), result.GetValue(_playerId), ct),
            WorldMapJsonContext.Default.WorldPlayerObservation, [A.Instance, A.Item], [_playerId]);
        Request(Add(map, "read", S.WorldMapReadDescription, (app, result, ct) => app.Client.WorldMaps.ReadMapAsync(Read(result), ct),
            WorldMapJsonContext.Default.SeedMapTile, options: [_request, _requestFile, _fillUnexplored]));
        Request(Add(map, "region", S.WorldMapRegionDescription, (app, result, ct) => app.Client.WorldMaps.ReadRegionAsync(Read(result, WorldMapJsonContext.Default.WorldRegionQuery), cancellationToken: ct),
            WorldMapJsonContext.Default.SeedRegionResult, options: [_request, _requestFile]));
        Request(Add(map, "structures", S.WorldMapStructuresDescription, (app, result, ct) => app.Client.WorldMaps.FindStructuresAsync(Read(result, WorldMapJsonContext.Default.WorldStructureQuery), ct),
            WorldMapJsonContext.Default.SeedStructureResult, options: [_request, _requestFile]));
        Request(Add(map, "scene", S.WorldMapSceneDescription, (app, result, ct) => app.Client.WorldMaps.InspectSceneAsync(Read(result, WorldMapJsonContext.Default.WorldRegionQuery), ct),
            WorldMapJsonContext.Default.WorldSceneInspection, options: [_request, _requestFile]));
        Request(Add(map, "predict-loot", S.WorldMapPredictLootDescription, (app, result, ct) => app.Client.WorldMaps.PredictContainerAsync(Read(result, WorldMapJsonContext.Default.WorldContainerQuery), ct),
            WorldMapJsonContext.Default.WorldContainerPrediction, options: [_request, _requestFile]));
        Request(Add(map, "export", S.WorldMapExportDescription, async (app, result, ct) =>
        {
            var tile = await app.Client.WorldMaps.ReadMapAsync(Read(result), ct).ConfigureAwait(false);
            return await SeedExplorerService.ExportAsync(tile, result.GetRequiredValue(_output), cancellationToken: ct).ConfigureAwait(false);
        }, SeedStoreJsonContext.Default.SeedMapExport, options: [_request, _requestFile, _output, _fillUnexplored]));
    }

    private LauncherCommand Request(LauncherCommand command)
    {
        command.Validators.Add(result =>
        {
            if ((result.GetValue(_request) is null) == (result.GetValue(_requestFile) is null)) result.AddError(S.SeedRequestChoiceError);
        });
        return command;
    }

    private WorldMapQuery Read(ParseResult result)
    {
        var query = Read(result, WorldMapJsonContext.Default.WorldMapQuery);
        if (result.GetValue(_fillUnexplored)) query.FillUnexplored = true;
        return query;
    }

    private T Read<T>(ParseResult result, JsonTypeInfo<T> metadata) where T : class
    {
        var json = result.GetValue(_request) ?? File.ReadAllText(result.GetRequiredValue(_requestFile)!);
        try { return JsonSerializer.Deserialize(json, metadata) ?? throw new ArgumentException(S.SeedRequestEmptyError); }
        catch (JsonException error) { throw new ArgumentException(string.Format(S.SeedRequestInvalidError, error.Message), error); }
    }

    private static WorldMapSelection Selection(ParseResult result) => new(result.GetRequiredValue(A.Instance), result.GetRequiredValue(A.Item));
}