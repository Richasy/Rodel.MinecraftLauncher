// Copyright (c) Richasy. All rights reserved.

using Rodel.MinecraftLauncher.Core.Downloads;
using Rodel.MinecraftLauncher.Core.Launch;
using Rodel.MinecraftLauncher.Core.Storage;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace Rodel.MinecraftLauncher.Core.GameBridge;

internal static class BridgeComponent
{
    private static readonly (string Key, string Owner, string Signature)[] Members =
    [
        ("clientInstance", "net.minecraft.client.Minecraft", "getInstance()"),
        ("clientPlayer", "net.minecraft.client.Minecraft", "player"),
        ("clientLevel", "net.minecraft.client.Minecraft", "level"),
        ("clientServer", "net.minecraft.client.Minecraft", "getSingleplayerServer()"),
        ("clientLocal", "net.minecraft.client.Minecraft", "isLocalServer()"),
        ("clientConnection", "net.minecraft.client.Minecraft", "getConnection()"),
        ("listenerConnection", "net.minecraft.client.multiplayer.ClientPacketListener", "getConnection()"),
        ("connectionMemory", "net.minecraft.network.Connection", "isMemoryConnection()"),
        ("entityUuid", "net.minecraft.world.entity.Entity", "getUUID()"),
        ("entityX", "net.minecraft.world.entity.Entity", "getX()"),
        ("entityY", "net.minecraft.world.entity.Entity", "getY()"),
        ("entityZ", "net.minecraft.world.entity.Entity", "getZ()"),
        ("entityLevel", "net.minecraft.world.entity.Entity", "level()"),
        ("entityHealth", "net.minecraft.world.entity.LivingEntity", "getHealth()"),
        ("entitySetHealth", "net.minecraft.world.entity.LivingEntity", "setHealth(float)"),
        ("serverPlayerList", "net.minecraft.server.MinecraftServer", "getPlayerList()"),
        ("listPlayer", "net.minecraft.server.players.PlayerList", "getPlayer(java.util.UUID)"),
        ("levelDimension", "net.minecraft.world.level.Level", "dimension()"),
        ("keyLocation", "net.minecraft.resources.ResourceKey", "location()"),
        ("levelTime", "net.minecraft.world.level.Level", "getDayTime()"),
        ("levelSetTime", "net.minecraft.server.level.ServerLevel", "setDayTime(long)")
    ];
    private static readonly (string Key, string Owner, string Signature)[] RegionMembers =
    [
        ("levelChunks", "net.minecraft.world.level.LevelAccessor", "getChunkSource()"),
        ("chunkNow", "net.minecraft.world.level.chunk.ChunkSource", "getChunkNow(int,int)"),
        ("levelBlock", "net.minecraft.world.level.BlockGetter", "getBlockState(net.minecraft.core.BlockPos)"),
        ("stateBlock", "net.minecraft.world.level.block.state.BlockBehaviour$BlockStateBase", "getBlock()"),
        ("registryKey", "net.minecraft.core.Registry", "getKey(java.lang.Object)"),
        ("registryBlock", "net.minecraft.core.registries.BuiltInRegistries", "BLOCK"),
        ("registryEntity", "net.minecraft.core.registries.BuiltInRegistries", "ENTITY_TYPE"),
        ("registryItem", "net.minecraft.core.registries.BuiltInRegistries", "ITEM"),
        ("levelEntities", "net.minecraft.client.multiplayer.ClientLevel", "entitiesForRendering()"),
        ("entityType", "net.minecraft.world.entity.Entity", "getType()"),
        ("playerInventory", "net.minecraft.world.entity.player.Player", "getInventory()"),
        ("inventorySize", "net.minecraft.world.Container", "getContainerSize()"),
        ("inventoryItem", "net.minecraft.world.Container", "getItem(int)"),
        ("stackItem", "net.minecraft.world.item.ItemStack", "getItem()"),
        ("stackCount", "net.minecraft.world.item.ItemStack", "getCount()")
    ];

    internal static async Task<(string Jar, string Bindings, string InputHooks)> PrepareAsync(LauncherPaths paths, string minecraft, string version, bool offline, CancellationToken ct,
        int javaMajor = 17, string? gameDirectory = null)
    {
        var metadata = await new VersionResolver().ResolveAsync(minecraft, version, ct).ConfigureAwait(false);
        var clientId = metadata["_clientJarVersion"]?.GetValue<string>() ?? version;
        var clientJar = SharedRuntimeLayout.Resolve(minecraft, "versions/" + clientId + "/" + clientId + ".jar");
        bool named;
        bool scopedLan;
        using (var client = ZipFile.OpenRead(clientJar))
        {
            named = client.GetEntry("net/minecraft/client/Minecraft.class") is not null;
            scopedLan = client.GetEntry("net/minecraft/server/MinecraftServer$MultiplayerScope.class") is not null;
        }

        var observation = javaMajor < 17 || (metadata["downloads"]?["client_mappings"] is null && !named);
        var directory = Path.Combine(paths.DependenciesDirectory, "game-bridge");
        Directory.CreateDirectory(directory);
        using var resource = typeof(BridgeComponent).Assembly.GetManifestResourceStream(observation ? "Rodel.GameBridge.Observation" : "Rodel.GameBridge.Agent")
            ?? throw new InvalidDataException("The packaged game bridge is missing.");
        using var content = new MemoryStream();
        await resource.CopyToAsync(content, ct).ConfigureAwait(false);
        var bytes = content.ToArray();
        var hash = SHA256.HashData(bytes);
        var jar = Path.Combine(directory, Convert.ToHexStringLower(hash) + ".jar");
        using (await ResourceLease.AcquireAsync(jar, ct).ConfigureAwait(false))
        {
            if (!File.Exists(jar) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(await File.ReadAllBytesAsync(jar, ct).ConfigureAwait(false)), hash))
                await ReplaceAsync(jar, bytes, ct).ConfigureAwait(false);
        }

        using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var hookStream = archive.GetEntry("rodel-input-hooks.jar")?.Open() ?? throw new InvalidDataException("The packaged input hook is missing.");
        using var hookContent = new MemoryStream();
        await hookStream.CopyToAsync(hookContent, ct).ConfigureAwait(false);
        var hookBytes = hookContent.ToArray();
        var hookHash = SHA256.HashData(hookBytes);
        var hookJar = Path.Combine(directory, Convert.ToHexStringLower(hookHash) + ".hooks.jar");
        using (await ResourceLease.AcquireAsync(hookJar, ct).ConfigureAwait(false))
        {
            if (!File.Exists(hookJar) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(await File.ReadAllBytesAsync(hookJar, ct).ConfigureAwait(false)), hookHash))
                await ReplaceAsync(hookJar, hookBytes, ct).ConfigureAwait(false);
        }

        string? mappingText = null;
        if (metadata["downloads"]?["client_mappings"] is JsonObject download)
        {
            var sha1 = download["sha1"]!.GetValue<string>().ToLowerInvariant();
            if (sha1.Length != 40 || !sha1.All(Uri.IsHexDigit)) throw new InvalidDataException("Invalid mapping digest.");
            var mapping = Path.Combine(directory, sha1 + ".mappings");
            var cached = File.Exists(mapping) && Convert.ToHexStringLower(SHA1.HashData(await File.ReadAllBytesAsync(mapping, ct).ConfigureAwait(false))) == sha1;
            if (!cached && RuntimeProfile.Loader(metadata).Kind is "forge" or "neoforge"
                && await BridgeSrgMappings.ReadOfficialClientMappingsAsync(minecraft, metadata, sha1, download["size"]?.GetValue<long>(), ct).ConfigureAwait(false) is { } installed)
            {
                await ReplaceAsync(mapping, installed, ct).ConfigureAwait(false);
                cached = true;
            }

            if (offline)
            {
                if (!cached)
                    throw new InvalidDataException("Official mappings are unavailable in the offline bridge cache.");
            }
            else if (!cached)
            {
                await new DownloadService(paths, NetworkSettings.Client(paths, TimeSpan.FromSeconds(15)))
                    .DownloadAsync(new Uri(download["url"]!.GetValue<string>()), mapping, sha1, download["size"]?.GetValue<long>(), cancellationToken: ct).ConfigureAwait(false);
            }

            mappingText = await File.ReadAllTextAsync(mapping, ct).ConfigureAwait(false);
        }
        else if (!observation && !named)
        {
            // An absent mapping is not evidence that an arbitrary old or future version uses named classes.
            throw new InvalidDataException("No verified named-class binding exists for this Minecraft version.");
        }

        var srgMappings = mappingText is not null && RuntimeProfile.Loader(metadata).Kind is "forge" or "neoforge"
            ? await BridgeSrgMappings.ReadAsync(minecraft, metadata, ct).ConfigureAwait(false) : null;
        var properties = observation ? CreateObservationBindings(mappingText, clientJar, gameDirectory ?? minecraft) : CreateBindings(mappingText, srgMappings, scopedLan);
        var bindingBytes = Encoding.UTF8.GetBytes(properties);
        var bindings = Path.Combine(directory, Convert.ToHexStringLower(SHA256.HashData(bindingBytes)) + ".properties");
        using (await ResourceLease.AcquireAsync(bindings, ct).ConfigureAwait(false))
        {
            if (!File.Exists(bindings) || !CryptographicOperations.FixedTimeEquals(SHA256.HashData(await File.ReadAllBytesAsync(bindings, ct).ConfigureAwait(false)), SHA256.HashData(bindingBytes)))
                await ReplaceAsync(bindings, bindingBytes, ct).ConfigureAwait(false);
        }

        return (jar, bindings, hookJar);
    }

    internal static string CreateBindings(string? mapping, string? srgMapping = null, bool namedScopedLan = false)
    {
        var (classes, members) = ReadSymbols(mapping);
        const string scopedPublish = "publishServer(net.minecraft.server.MinecraftServer$MultiplayerScope,int)";
        var scopedLan = mapping is null ? namedScopedLan
            : members.ContainsKey(("net.minecraft.client.server.IntegratedServer", scopedPublish))
                && members.ContainsKey(("net.minecraft.server.MinecraftServer$MultiplayerScope", "LAN"));

        string Descriptor(string value) => value.EndsWith("[]", StringComparison.Ordinal) ? "[" + Descriptor(value[..^2]) : value switch
        {
            "void" => "V",
            "boolean" => "Z",
            "byte" => "B",
            "char" => "C",
            "short" => "S",
            "int" => "I",
            "long" => "J",
            "float" => "F",
            "double" => "D",
            _ => "L" + classes.GetValueOrDefault(value, value).Replace('.', '/') + ";"
        };
        var srg = BridgeSrgMappings.Parse(srgMapping);
        string RuntimeType(string name) => srgMapping is null ? classes.GetValueOrDefault(name, name) : name;
        var result = new StringBuilder();
        result.AppendLine("clientClass=" + RuntimeType("net.minecraft.client.Minecraft"));
        result.AppendLine("clockApi=" + (mapping is null ? "true" : "false"));
        result.AppendLine("namedApi=" + (mapping is null ? "true" : "false"));
        var region = mapping is null || RegionMembers.All(value => members.ContainsKey((value.Owner, value.Signature)));
        result.AppendLine("regionApi=" + (region ? "true" : "false"));
        result.AppendLine("blockPosClass=" + RuntimeType("net.minecraft.core.BlockPos"));
        foreach (var typeName in GamePlayBindings.Types)
            result.AppendLine("type." + typeName + "=" + (mapping is null && typeName == "net.minecraft.world.inventory.ClickType" ? "net.minecraft.world.inventory.ContainerInput" : RuntimeType(typeName)));
        var gameplay = GamePlayBindings.Members.Select(value => value.Key == "serverPublish" && scopedLan
            ? (value.Key, value.Owner, Signature: scopedPublish) : value)
            .Where(value => value.Key != "lanScope" || scopedLan)
            .Select(value => value.Key == "worldAllowCommands" && mapping is not null && !members.ContainsKey((value.Owner, value.Signature))
            ? (value.Key, value.Owner, Signature: "getAllowCommands()") : value)
            .Where(value => mapping is null || members.ContainsKey((value.Owner, value.Signature)));
        foreach (var (key, declaredOwner, declaredSignature) in Members.Concat(region ? RegionMembers : []).Concat(gameplay))
        {
            var signature = mapping is null ? key switch
            {
                "gameClick" => "handleContainerInput(int,int,int,net.minecraft.world.inventory.ContainerInput,net.minecraft.world.entity.player.Player)",
                "levelEntity" => "getEntityInAnyDimension(java.util.UUID)",
                _ => declaredSignature
            } : declaredSignature;
            var namedOwner = mapping is null ? key switch
            {
                "playerCommandSource" => "net.minecraft.server.level.ServerPlayer",
                "levelEntity" => "net.minecraft.world.level.Level",
                _ => declaredOwner
            } : declaredOwner;
            var name = signature.Split('(')[0];
            var type = string.Empty;
            if (mapping is not null)
            {
                if (!members.TryGetValue((namedOwner, signature), out var member))
                    throw new InvalidDataException("Required Minecraft binding is absent: " + namedOwner + "." + signature);
                (name, type) = member;
            }
            else if (key == "keyLocation")
            {
                name = "identifier";
            }
            else if (key == "levelTime")
            {
                name = "getDefaultClockTime";
            }

            var argumentStart = signature.IndexOf('(');
            var descriptor = argumentStart < 0 ? Descriptor(type) : "(" + string.Concat(signature[(argumentStart + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Descriptor)) + ")" + Descriptor(type);
            if (srgMapping is not null)
            {
                var srgKey = (classes[namedOwner], name, argumentStart < 0 ? string.Empty : descriptor);
                if (!srg.TryGetValue(srgKey, out name)) throw new InvalidDataException("Required SRG binding is absent: " + namedOwner + "." + signature);
            }

            result.AppendLine(key + "=" + name);
            result.AppendLine(key + ".owner=" + RuntimeType(namedOwner));
            if (argumentStart >= 0)
            {
                result.AppendLine(key + ".parameters=" + string.Join(',', signature[(argumentStart + 1)..^1].Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(RuntimeType)));
            }

            if (mapping is not null)
            {
                result.AppendLine(key + ".descriptor=" + descriptor);
            }
        }

        return result.ToString();
    }

    private static (Dictionary<string, string> Classes, Dictionary<(string Owner, string Signature), (string Name, string Type)> Members) ReadSymbols(string? mapping)
    {
        var classes = new Dictionary<string, string>(StringComparer.Ordinal);
        var members = new Dictionary<(string Owner, string Signature), (string Name, string Type)>();
        string? owner = null;
        foreach (var line in (mapping ?? string.Empty).Split('\n'))
        {
            if (line.Length == 0 || line[0] == '#') continue;
            var split = line.Trim().Split(" -> ", 2, StringSplitOptions.None);
            if (split.Length != 2) continue;
            if (line[0] != ' ')
            {
                owner = split[0];
                classes[owner] = split[1].TrimEnd('\r', ':');
                continue;
            }

            if (owner is null) continue;
            var signature = split[0];
            var space = signature.IndexOf(' ');
            if (space < 0) continue;
            var type = signature[..space];
            type = type[(type.LastIndexOf(':') + 1)..];
            var member = signature[(space + 1)..];
            var end = member.IndexOf(')');
            if (end >= 0) member = member[..(end + 1)];
            members[(owner, member)] = (split[1].Trim(), type);
        }

        return (classes, members);
    }

    private static readonly string[] ObservationRoles = ["clientPlayer", "clientLevel", "clientServer", "entityUuid", "entityX", "entityY", "entityZ", "levelDimension", "keyLocation", "entityYaw"];
    private static readonly (string Key, string Owner, string Signature)[] ObservationFields =
    [
        ("entityYawField", "net.minecraft.world.entity.Entity", "yRot"),
        ("entityDimension", "net.minecraft.world.entity.Entity", "dimension"),
        ("levelDimensionField", "net.minecraft.world.level.Level", "dimension"),
        ("providerType", "net.minecraft.world.level.dimension.Dimension", "getType()"),
        ("dimensionId", "net.minecraft.world.level.dimension.DimensionType", "getId()"),
        ("dimensionLocation", "net.minecraft.world.level.dimension.DimensionType", "getName()"),
        ("dimensionLocationStatic", "net.minecraft.world.level.dimension.DimensionType", "getName(net.minecraft.world.level.dimension.DimensionType)")
    ];

    internal static string CreateObservationBindings(string? mapping, string clientJar, string gameDirectory)
    {
        var (classes, members) = ReadSymbols(mapping);
        var result = new StringBuilder();
        static string Escape(string value) => value.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\r", "\\r", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);
        result.AppendLine("clientJar=" + Escape(Path.GetFullPath(clientJar)));
        result.AppendLine("gameDirectory=" + Escape(Path.GetFullPath(gameDirectory)));
        if (classes.TryGetValue("net.minecraft.client.Minecraft", out var client)) result.AppendLine("clientClass=" + client);
        if (classes.TryGetValue("net.minecraft.world.entity.Entity", out var entity)) result.AppendLine("entityClass=" + entity);
        foreach (var (key, owner, signature) in Members.Concat(GamePlayBindings.Members).Where(member => ObservationRoles.Contains(member.Key, StringComparer.Ordinal)).Concat(ObservationFields))
            if (members.TryGetValue((owner, signature), out var member)) result.AppendLine(key + "=" + member.Name);
        return result.ToString();
    }

    private static async Task ReplaceAsync(string path, byte[] content, CancellationToken ct)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllBytesAsync(temporary, content, ct).ConfigureAwait(false);
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }
}