// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Application.AI;
using Rodel.MinecraftLauncher.Core.GameBridge;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Schema;
using System.Text.Json.Serialization.Metadata;

namespace Rodel.MinecraftLauncher.Core.Tools;

internal sealed class ToolInputContract<T>(JsonTypeInfo<T> typeInfo, bool launcherConventions = true) where T : class
{
    private sealed record Field(string Description, long? Minimum = null, long? Maximum = null, int? MinItems = null, string[]? AllowedValues = null);

    private static readonly Dictionary<string, Field> RepairFields = new()
    {
        ["repairId"] = new("Incident identifier returned by the host; cannot select another instance."),
        ["kind"] = new("Exact repair action: disable_mod, java, memory or dependencies. dependencies resolves the complete observed missing-dependency group automatically.", AllowedValues: AiRepairPlan.Kinds),
        ["target"] = new("For disable_mod, the exact installed archive fileName from repair_show, not a mod ID. For java, a discovered executable path. Omit for memory and dependencies; a dependency ID or slug here cannot select a different dependency group."),
        ["memoryMb"] = new("Proposed maximum heap in MiB; only for memory and in the direction supported by the actual error.", 512, 16384),
        ["reason"] = new("Bounded explanation linking this candidate to observed loader/crash evidence; does not grant permission or select dependency versions.")
    };

    private static readonly IReadOnlyDictionary<string, Field> Fields = new Dictionary<string, Field>
    {
        [nameof(InstanceInput.InstanceId)] = new("Existing instance identifier; omit where optional for launcher/global scope."),
        [nameof(CapabilitiesInput.ToolName)] = new("Exact tool name; returns its complete input schema."),
        [nameof(CapabilitiesInput.Topic)] = new("CLI help topic such as content, pack, instance, config, launch or job."),
        [nameof(ExecuteInput.Arguments)] = new("CLI argument tokens, without executable name or --data-dir; never interpreted by a shell."),
        [nameof(PageInput.Offset)] = new("Zero-based item or JSON-text character offset.", 0, int.MaxValue),
        [nameof(PageInput.Limit)] = new("Maximum returned items, 1 through 100.", 1, 100),
        [nameof(RuntimeChangeInput.MinecraftVersion)] = new("Base Minecraft content version; not a loader profile ID. Omit to resolve from the instance."),
        [nameof(RuntimeChangeInput.Loader)] = new("Loader filter/selection; custom strings remain allowed. For runtime change, none selects vanilla."),
        [nameof(RuntimeChangeInput.LoaderVersion)] = new("Exact loader version."),
        [nameof(RuntimeChangeInput.Version)] = new("Minecraft launch profile ID; local/custom identifiers remain allowed."),
        [nameof(RuntimeChangeInput.JavaPath)] = new("Explicit Java executable; custom runtimes remain allowed."),
        [nameof(RuntimeChangeInput.Plan)] = new("Read-only proposal; does not apply the change."),
        [nameof(RuntimeChangeInput.Offline)] = new("Use installed files without automatic downloads."),
        [nameof(RuntimeChangeInput.Background)] = new("Run as a persistent background job; inspect job_show for its actual outcome."),
        [nameof(RuntimeOperationInput.OperationId)] = new("Saved runtime operation identifier."),
        [nameof(EncodedInstanceInput.Encoding)] = new("Optional encoding for BOM-less legacy options.txt."),
        [nameof(GameSettingsSetInput.Changes)] = new("Actual setting keys and values. Game options accept scalar raw values and null removes a key; launcher config preserves arbitrary JSON and explicit null."),
        [nameof(ConfigPatchInput.RemoveKeys)] = new("Launcher override keys to remove, restoring global/default inheritance. Other keys remain unchanged."),
        [nameof(GameSettingsSetInput.ExpectedFingerprint)] = new("Current options.txt fingerprint to preserve intervening edits."),
        [nameof(GameSettingsApplyInput.Overwrite)] = new("Explicitly apply requested values despite changed keys."),
        [nameof(ResourcepackSelectInput.Names)] = new("Ordered resource pack names; an empty array is permitted."),
        [nameof(ResourcepackSelectInput.Replace)] = new("Replace the complete selected list instead of preserving existing selections."),
        [nameof(ResourcepackSelectInput.AllowIncompatible)] = new("Explicitly accept requested incompatible packs."),
        [nameof(ShaderpackListInput.Engine)] = new("Shader configuration owner: iris, oculus or optifine; omit to discover from actual configuration and installed engine mods."),
        [nameof(ShaderpackSelectInput.FileName)] = new("Installed shader-pack file identity; omit or use null to disable shaders."),
        [nameof(InstanceCreateInput.Name)] = new("Instance display name."),
        [nameof(ConfigGetInput.Key)] = new("Launcher setting key."),
        [nameof(ConfigSetInput.Value)] = new("Actual JSON setting value: string, number, boolean, array, object or null."),
        [nameof(ContentSearchInput.Query)] = new("Community content search text."),
        [nameof(ContentSearchInput.Platform)] = new("Defaults to modrinth; default CurseForge online provider is unavailable."),
        [nameof(ContentSearchInput.ProjectType)] = new("Usually mod, resourcepack, shader or modpack; defaults to mod."),
        [nameof(ContentSearchInput.Index)] = new("Sort order; defaults to relevance."),
        [nameof(ContentListInput.Kind)] = new("Content directory kind, usually mods, resourcepacks, shaderpacks or datapacks; defaults to mods."),
        [nameof(ContentListInput.World)] = new("World name/path for world-specific content or singleplayer quick play."),
        [nameof(ContentInput.ProjectId)] = new("Platform project ID or slug."),
        [nameof(ContentInput.VersionId)] = new("Exact platform content version ID; omit to resolve a compatible current version."),
        [nameof(ContentInput.Channel)] = new("Optional update channel: release, beta or alpha."),
        [nameof(ContentInput.IncludeDependencies)] = new("Resolve required dependencies; false permits manual dependency management."),
        [nameof(ContentInput.VersionSelections)] = new("Project IDs to exact version IDs; project IDs cannot contain '='."),
        [nameof(ContentReviewInput.SourceSelections)] = new("Local file names to explicitly selected Modrinth project IDs from sourceCandidates. Native mod declarations are checked before replacement; selection does not apply the update."),
        [nameof(ContentInstallInput.ExpectedLockFingerprint)] = new("Reviewed lockFingerprint; changed tracking rejects the stale baseline before network work."),
        [nameof(ContentReviewInput.Files)] = new("Selected installed content files; at least one is required.", MinItems: 1),
        [nameof(ContentReviewInput.UseSourceContext)] = new("Use recorded source compatibility filters instead of resolving the current runtime."),
        [nameof(ContentApplyInput.ReviewId)] = new("Saved exact content review identifier."),
        [nameof(ContentApplyInput.Confirm)] = new("True only after the user confirms the exact reviewed list."),
        [nameof(JobInput.JobId)] = new("Background launcher job identifier."),
        [nameof(DiagnoseInput.SessionId)] = new("Game session identifier returned by launch or process_list."),
        [nameof(DiagnoseInput.ReportPath)] = new("Local Minecraft or JVM crash report; diagnose requires this, sessionId or instanceId."),
        [nameof(DiagnoseInput.Verify)] = new("Verify installation hashes; may take longer."),
        [nameof(LaunchInput.Player)] = new("One-shot local player name."),
        [nameof(LaunchInput.MemoryMb)] = new("Maximum Java heap memory in MiB; omit to inherit settings.", 1, int.MaxValue),
        [nameof(LaunchInput.Width)] = new("One-shot window width."),
        [nameof(LaunchInput.Height)] = new("One-shot window height."),
        [nameof(LaunchInput.Fullscreen)] = new("One-shot fullscreen override; omit to inherit settings."),
        [nameof(LaunchInput.Server)] = new("Multiplayer address or favorite."),
        [nameof(LaunchInput.AccountId)] = new("Explicit saved offline or Microsoft identity. Omit to inherit instance/global selection; no credentials are accepted."),
        [nameof(LaunchInput.ServerAddress)] = new("Concrete multiplayer address. Takes precedence over server and never reads or reinterprets favorites."),
        [nameof(LaunchInput.GameArguments)] = new("Additional game arguments as separate tokens."),
        [nameof(LaunchInput.Environment)] = new("Environment overrides: scalar values, empty string to retain an empty value, null to remove a variable."),
        [nameof(LaunchInput.MemoryMode)] = new("manual or auto; explicit memoryMb wins."),
        [nameof(LaunchInput.LogEncoding)] = new("Explicit stdout/stderr encoding; does not change file.encoding."),
        [nameof(LaunchInput.GameLanguage)] = new("Minecraft locale to apply before process start, such as zh_cn, en_us or ar_sa. Omit to preserve the game language."),
        [nameof(LaunchInput.LoggingCompatibility)] = new("Known legacy logging compatibility override; defaults enabled."),
        [nameof(ProcessStopInput.Force)] = new("Force termination only when explicitly requested; no automatic escalation."),
        [nameof(ReadLogsInput.Tail)] = new("Maximum tail lines when no cursor is supplied."),
        [nameof(ReadLogsInput.Bytes)] = new("Total raw log byte budget.", 8, 1048576),
        [nameof(ReadLogsInput.StdoutOffset)] = new("Absolute retained stdout byte offset.", 0, long.MaxValue),
        [nameof(ReadLogsInput.StderrOffset)] = new("Absolute retained stderr byte offset.", 0, long.MaxValue),
        [nameof(ReadLogsInput.Format)] = new("text (default) or events for XML/text structured records."),
        [nameof(ReadLogsInput.MinimumLevel)] = new("Optional events level filter."),
        [nameof(ReadLogsInput.ParserState)] = new("Returned parserState for contiguous event cursors; preserve all unknown fields."),
        [nameof(ResultReadInput.ResultId)] = new("Saved result identifier returned by a previous tool result."),
        [nameof(ResultReadInput.Pointer)] = new("RFC 6901 JSON Pointer, e.g. /data/nodes; empty means full result."),
        [nameof(ResultReadInput.Characters)] = new("UTF-16 JSON-text characters per chunk.", 256, 8000),
        [nameof(ResultReadInput.PropertyOffset)] = new("Object property-name offset; follow nextPropertyOffset.", 0, int.MaxValue)
    };

    /// <summary>Gets the input schema exported from the same typed metadata and constraints used for validation.</summary>
    public JsonElement Schema { get; } = Export(typeInfo, launcherConventions);

    /// <summary>Reads typed arguments and validates declared null, collection, range, and minimum-item constraints.</summary>
    /// <param name="input">The argument object after undeclared top-level fields have been handled by the dispatcher.</param>
    /// <returns>The validated input model; malformed or unsupported values produce an argument exception.</returns>
    public T Deserialize(JsonObject input)
    {
        try
        {
            // Nullable optional CLR members represent omission, not an explicit JSON null.
            foreach (var property in typeInfo.Properties)
            {
                if (launcherConventions && input.TryGetPropertyValue(property.Name, out var supplied) && supplied is null && property.PropertyType != typeof(JsonElement))
                    throw new ArgumentException(property.Name + " must not be null.");
            }

            var value = input.Deserialize(typeInfo) ?? throw new ArgumentException("Tool arguments must be an object.");
            foreach (var property in typeInfo.Properties)
            {
                var member = property.Get?.Invoke(value);
                if ((member is IEnumerable<string> strings && strings.Any(item => item is null))
                    || (member is IReadOnlyDictionary<string, string> map && map.Values.Any(item => item is null)))
                {
                    throw new ArgumentException(property.Name + " must contain only strings.");
                }

                var field = launcherConventions ? Find(property.Name) : null;
                if (field is null || member is null)
                    continue;
                if (field.AllowedValues is { } allowed && member is string text && !allowed.Contains(text, StringComparer.Ordinal))
                    throw new ArgumentException(property.Name + " must be one of: " + string.Join(", ", allowed) + ".");
                if (member is int or long)
                {
                    var number = member is int integer ? integer : (long)member;
                    if (number < field.Minimum || number > field.Maximum)
                        throw new ArgumentException($"{property.Name} must be an integer from {field.Minimum} through {field.Maximum}.");
                }

                if (field.MinItems is { } count && member is string[] items && items.Length < count)
                    throw new ArgumentException(property.Name + " contains too few items.");
            }

            return value;
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("Invalid tool arguments: " + ex.Message, ex);
        }
    }

    private static Field? Find(string jsonName) => typeof(T) == typeof(AiRepairPlan) && RepairFields.TryGetValue(jsonName, out var repairField)
        ? repairField : Fields.FirstOrDefault(field => JsonNamingPolicy.CamelCase.ConvertName(field.Key) == jsonName).Value;

    private static JsonElement Export(JsonTypeInfo<T> info, bool launcherConventions)
    {
        var schema = info.GetJsonSchemaAsNode(new JsonSchemaExporterOptions
        {
            TreatNullObliviousAsNonNullable = true,
            TransformSchemaNode = (context, node) =>
            {
                if (context.PropertyInfo is { } gameProperty && node is JsonObject gameField)
                {
                    if (info.Type == typeof(GameRegionInput))
                    {
                        if (gameProperty.Name is "sizeX" or "sizeY" or "sizeZ") { gameField["minimum"] = 1; gameField["maximum"] = GameBridgeLimits.MaximumRegionEdge; }

                        if (gameProperty.Name is "x" or "z") { gameField["minimum"] = -GameBridgeLimits.MaximumHorizontalCoordinate; gameField["maximum"] = GameBridgeLimits.MaximumHorizontalCoordinate; }

                        if (gameProperty.Name == "y") { gameField["minimum"] = -GameBridgeLimits.MaximumVerticalCoordinate; gameField["maximum"] = GameBridgeLimits.MaximumVerticalCoordinate; }
                    }

                    if (info.Type == typeof(GameEditInput))
                    {
                        if (gameProperty.Name == "health") { gameField["minimum"] = GameBridgeLimits.MinimumHealth; gameField["maximum"] = GameBridgeLimits.MaximumHealth; }

                        if (gameProperty.Name == "kind") gameField["enum"] = new JsonArray([.. GameBridgeLimits.EditKinds.Select(value => (JsonNode?)JsonValue.Create(value))]);
                        if (gameProperty.Name is "ticks" or "interactionDelayTicks") { gameField["minimum"] = 1; gameField["maximum"] = GameBridgeLimits.MaximumControlTicks; }

                        if (gameProperty.Name == "hotbar") { gameField["minimum"] = 0; gameField["maximum"] = 8; }

                        if (gameProperty.Name == "yaw") { gameField["minimum"] = -360; gameField["maximum"] = 360; }

                        if (gameProperty.Name == "pitch") { gameField["minimum"] = -90; gameField["maximum"] = 90; }

                        if (gameProperty.Name == "keys") { gameField["maxItems"] = GameBridgeLimits.ControlKeys.Count; gameField["uniqueItems"] = true; gameField["items"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray([.. GameBridgeLimits.ControlKeys.Select(value => (JsonNode?)JsonValue.Create(value))]) }; }

                        if (gameProperty.Name == "mode") gameField["enum"] = new JsonArray([.. GameBridgeLimits.ClickModes.Select(value => (JsonNode?)JsonValue.Create(value))]);
                    }

                    if (info.Type == typeof(GameQueryInput))
                    {
                        if (gameProperty.Name == "kind") gameField["enum"] = new JsonArray([.. GameBridgeLimits.QueryKinds.Select(value => (JsonNode?)JsonValue.Create(value))]);
                        if (gameProperty.Name == "limit") { gameField["minimum"] = 1; gameField["maximum"] = GameBridgeLimits.MaximumQueryItems; }

                        if (gameProperty.Name == "offset") gameField["minimum"] = 0;
                    }
                }

                if (context.TypeInfo.Type == typeof(ScalarValue))
                    return ScalarSchema();
                // The exporter elides unconstrained converter schemas in dictionaries before applying child transforms.
                if (context.TypeInfo.Type == typeof(Dictionary<string, ScalarValue>) && node is JsonObject dictionary)
                    dictionary["additionalProperties"] = ScalarSchema();
                if (info.Type == typeof(SkinCreateInput) && context.PropertyInfo?.Name == "size" && node is JsonObject skinSize)
                    skinSize["enum"] = new JsonArray([.. Rodel.Minecraft.Skins.SkinLayout.SupportedSizes.Select(size => (JsonNode?)JsonValue.Create(size))]);
                if (!launcherConventions || context.PropertyInfo is not { } property)
                    return node;
                var obj = node as JsonObject ?? [];
                if (property.PropertyType != typeof(JsonElement) && obj["type"] is JsonArray typesWithNull)
                {
                    var nonNull = typesWithNull.Where(type => type!.GetValue<string>() != "null").Select(type => type!.DeepClone()).ToArray();
                    obj["type"] = nonNull.Length == 1 ? nonNull[0] : new JsonArray(nonNull);
                }

                if (obj.ContainsKey("default") && obj["default"] is null)
                    obj.Remove("default");
                if (Find(property.Name) is { } field)
                {
                    obj["description"] = field.Description;
                    if (field.Minimum is { } minimum)
                        obj["minimum"] = minimum;
                    if (field.Maximum is { } maximum)
                        obj["maximum"] = maximum;
                    if (field.MinItems is { } count)
                        obj["minItems"] = count;
                    if (field.AllowedValues is { } allowed)
                        obj["enum"] = new JsonArray([.. allowed.Select(value => (JsonNode?)JsonValue.Create(value))]);
                }

                return obj;
            }
        });
        return JsonSerializer.SerializeToElement(schema, ToolInputJsonContext.Default.JsonNode);
    }

    private static JsonObject ScalarSchema()
    {
        var types = ScalarValue.AllowedKinds.Select(kind => kind switch
        {
            JsonValueKind.String => "string",
            JsonValueKind.Number => "number",
            JsonValueKind.True or JsonValueKind.False => "boolean",
            JsonValueKind.Null => "null",
            _ => throw new InvalidOperationException("Unsupported scalar kind.")
        }).Distinct().Select(type => (JsonNode?)JsonValue.Create(type)).ToArray();
        return new JsonObject { ["type"] = new JsonArray(types) };
    }
}