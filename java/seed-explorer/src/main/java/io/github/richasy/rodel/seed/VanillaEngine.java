package io.github.richasy.rodel.seed;

import static io.github.richasy.rodel.seed.Protocol.*;

import io.github.richasy.rodel.seed.Protocol.Position;

import net.minecraft.SharedConstants;
import net.minecraft.core.*;
import net.minecraft.core.registries.*;
import net.minecraft.resources.*;
import net.minecraft.server.Bootstrap;
import net.minecraft.server.packs.*;
import net.minecraft.server.packs.repository.ServerPacksSource;
import net.minecraft.server.packs.resources.MultiPackResourceManager;
import net.minecraft.util.datafix.DataFixers;
import net.minecraft.world.level.*;
import net.minecraft.world.level.biome.*;
import net.minecraft.world.level.chunk.*;
import net.minecraft.world.level.dimension.BuiltinDimensionTypes;
import net.minecraft.world.level.levelgen.*;
import net.minecraft.world.level.levelgen.densityfunction.SamplerContext;
import net.minecraft.world.level.levelgen.structure.*;
import net.minecraft.world.level.levelgen.structure.placement.*;
import net.minecraft.world.level.levelgen.structure.templatesystem.StructureTemplateManager;
import net.minecraft.world.level.material.Fluids;
import net.minecraft.world.level.storage.LevelStorageSource;

import java.nio.file.Path;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.CancellationException;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/**
 * Version-specific adapter. All climate, terrain, placement and structure selection execute vanilla
 * code.
 */
final class VanillaEngine implements AutoCloseable {
    private final MultiPackResourceManager resources;
    private final LevelStorageSource.LevelStorageAccess storage;
    private final RegistryAccess.Frozen registries;
    private final StructureTemplateManager templates;
    private final PalettedContainerFactory containers;
    private final LinkedHashMap<String, Context> contexts = new LinkedHashMap<>(4, .75f, true);
    private final LinkedHashMap<String, Context> samplingContexts =
            new LinkedHashMap<>(4, .75f, true);
    private final LinkedHashMap<String, Position> spawns = new LinkedHashMap<>(16, .75f, true);
    private final VanillaSpawn spawnWorker;
    private final ExecutorService mapSampler =
            Runtime.getRuntime().availableProcessors() > 1
                    ? Executors.newSingleThreadExecutor(
                            action -> {
                                var thread = new Thread(action, "seed-map-sampler");
                                thread.setDaemon(true);
                                return thread;
                            })
                    : null;
    private final Catalog catalog;
    private long deadline;

    VanillaEngine(Path directory) throws Exception {
        spawnWorker = new VanillaSpawn(directory.resolve("spawns"));
        SharedConstants.tryDetectVersion();
        Bootstrap.bootStrap();
        resources =
                new MultiPackResourceManager(
                        PackType.SERVER_DATA,
                        List.of(ServerPacksSource.createVanillaPackSource().fullResources()));
        storage = LevelStorageSource.createDefault(directory).createAccess("templates");
        var builtins = RegistryAccess.fromRegistryOfRegistries(BuiltInRegistries.REGISTRY);
        var tags = net.minecraft.tags.TagLoader.loadTagsForExistingRegistries(resources, builtins);
        tags.forEach(Registry.PendingTags::apply);
        var dynamic =
                RegistryDataLoader.load(
                                resources,
                                net.minecraft.tags.TagLoader.buildUpdatedLookups(builtins, tags),
                                RegistryDataLoader.WORLD_REGISTRIES,
                                Runnable::run)
                        .join();
        registries =
                new RegistryAccess.ImmutableRegistryAccess(
                                java.util.stream.Stream.concat(
                                        builtins.registries(), dynamic.registries()))
                        .freeze();
        templates =
                new StructureTemplateManager(
                        resources, storage, DataFixers.getDataFixer(), BuiltInRegistries.BLOCK);
        containers = PalettedContainerFactory.create(registries);
        catalog = createCatalog();
    }

    private static String biomeName(Holder<Biome> biome) {
        return biome.unwrapKey().orElseThrow().identifier().getPath();
    }

    private BiomeSource source(World world) {
        if ("overworld".equals(world.dimension()) && "single_biome".equals(world.preset()))
            return new FixedBiomeSource(
                    registries
                            .lookupOrThrow(Registries.BIOME)
                            .getOrThrow(
                                    ResourceKey.create(
                                            Registries.BIOME,
                                            Identifier.withDefaultNamespace(world.biome()))));
        if ("the_end".equals(world.dimension()))
            return TheEndBiomeSource.create(registries.lookupOrThrow(Registries.BIOME));
        return MultiNoiseBiomeSource.createFromPreset(
                registries
                        .lookupOrThrow(Registries.MULTI_NOISE_BIOME_SOURCE_PARAMETER_LIST)
                        .getOrThrow(
                                "the_nether".equals(world.dimension())
                                        ? MultiNoiseBiomeSourceParameterLists.NETHER
                                        : MultiNoiseBiomeSourceParameterLists.OVERWORLD));
    }

    private ResourceKey<NoiseGeneratorSettings> settings(World world) {
        if ("the_nether".equals(world.dimension())) return NoiseGeneratorSettings.NETHER;
        if ("the_end".equals(world.dimension())) return NoiseGeneratorSettings.END;
        return switch (world.preset()) {
            case "large_biomes" -> NoiseGeneratorSettings.LARGE_BIOMES;
            case "amplified" -> NoiseGeneratorSettings.AMPLIFIED;
            default -> NoiseGeneratorSettings.OVERWORLD;
        };
    }

    Catalog catalog() {
        return catalog;
    }

    /**
     * Resolves color metadata for frozen caller-supplied states and biomes without generating or
     * opening a world.
     */
    RegionTints tint(FrozenTintQuery query, AtomicBoolean cancelled) throws Exception {
        return new VanillaTint(registries).frozen(query, cancelled);
    }

    private Catalog createCatalog() {
        var dimensions = new ArrayList<DimensionEntry>();
        var sources = new LinkedHashMap<String, Set<Holder<Biome>>>();
        for (String id : List.of("overworld", "the_nether", "the_end")) {
            var world = new World(id, "normal", null);
            var biomes = source(world).possibleBiomes();
            sources.put(id, biomes);
            var height = height(world);
            dimensions.add(
                    new DimensionEntry(
                            id,
                            height.getMinY(),
                            height.getHeight(),
                            biomes.stream().map(VanillaEngine::biomeName).sorted().toList()));
        }
        var biomes =
                registries
                        .lookupOrThrow(Registries.BIOME)
                        .listElements()
                        .map(
                                holder ->
                                        new BiomeEntry(
                                                biomeName(holder),
                                                sources.entrySet().stream()
                                                        .filter(
                                                                entry ->
                                                                        entry.getValue()
                                                                                .contains(holder))
                                                        .map(Map.Entry::getKey)
                                                        .toList()))
                        .sorted(Comparator.comparing(BiomeEntry::id))
                        .toList();
        var structures =
                registries.lookupOrThrow(Registries.STRUCTURE).entrySet().stream()
                        .map(
                                entry ->
                                        new StructureEntry(
                                                entry.getKey().identifier().getPath(),
                                                sources.entrySet().stream()
                                                        .filter(
                                                                dimension ->
                                                                        entry
                                                                                .getValue()
                                                                                .biomes()
                                                                                .stream()
                                                                                .anyMatch(
                                                                                        dimension
                                                                                                        .getValue()
                                                                                                ::contains))
                                                        .map(Map.Entry::getKey)
                                                        .toList(),
                                                entry.getValue().biomes().stream()
                                                        .map(VanillaEngine::biomeName)
                                                        .sorted()
                                                        .toList()))
                        .sorted(Comparator.comparing(StructureEntry::id))
                        .toList();
        var defaults = new TreeMap<String, Map<String, String>>();
        for (var entry : BuiltInRegistries.BLOCK.entrySet()) {
            var state = entry.getValue().defaultBlockState();
            var values = new TreeMap<String, String>();
            for (var property : state.getProperties())
                values.put(property.getName(), defaultValue(state, property));
            defaults.put(entry.getKey().identifier().toString(), values);
        }
        return new Catalog(
                SharedConstants.getCurrentVersion().id(), dimensions, biomes, structures, defaults, true);
    }

    private static <T extends Comparable<T>> String defaultValue(
            net.minecraft.world.level.block.state.BlockState state,
            net.minecraft.world.level.block.state.properties.Property<T> property) {
        return property.getName(state.getValue(property));
    }

    private Context context(World world, String text) {
        return context(contexts, world, text);
    }

    private Context context(LinkedHashMap<String, Context> cache, World world, String text) {
        Objects.requireNonNull(world, "World settings are required.");
        if (!List.of("overworld", "the_nether", "the_end").contains(world.dimension()))
            throw new IllegalArgumentException("Unknown original dimension.");
        if (!List.of("normal", "large_biomes", "amplified", "single_biome")
                .contains(world.preset()))
            throw new IllegalArgumentException("Unknown original world preset.");
        long seed = Long.parseLong(text);
        String key = world.toString() + "/" + seed;
        var found = cache.get(key);
        if (found != null) return found;
        var holder =
                registries.lookupOrThrow(Registries.NOISE_SETTINGS).getOrThrow(settings(world));
        var source = source(world);
        var generator = new NoiseBasedChunkGenerator(source, holder);
        var random =
                RandomState.create(
                        registries.lookupOrThrow(Registries.NOISE), seed, holder.value());
        var biomeManager =
                new BiomeManager(
                        source.createResolver(
                                random.createClimateSampler(SamplerContext.EMPTY_UNCACHED)),
                        BiomeManager.obfuscateSeed(seed));
        var height = height(world);
        var structures =
                generator.createState(
                        registries.lookupOrThrow(Registries.STRUCTURE_SET), random, seed);
        var manager = new StructureManager(null, new WorldOptions(seed, true, false), null);
        var result =
                new Context(
                        world,
                        seed,
                        source,
                        generator,
                        random,
                        biomeManager,
                        height,
                        structures,
                        manager);
        cache.put(key, result);
        if (cache.size() > 4) cache.remove(cache.keySet().iterator().next());
        return result;
    }

    private LevelHeightAccessor height(World world) {
        var key =
                switch (world.dimension()) {
                    case "the_nether" -> BuiltinDimensionTypes.NETHER;
                    case "the_end" -> BuiltinDimensionTypes.END;
                    default -> BuiltinDimensionTypes.OVERWORLD;
                };
        var type = registries.lookupOrThrow(Registries.DIMENSION_TYPE).getOrThrow(key).value();
        return LevelHeightAccessor.create(type.minY(), type.height());
    }

    private void check(AtomicBoolean cancelled) {
        if (cancelled.get()
                || Thread.currentThread().isInterrupted()
                || (deadline != 0 && System.nanoTime() >= deadline))
            throw new CancellationException();
    }

    MapResult map(MapQuery query, AtomicBoolean cancelled) {
        deadline = 0;
        Objects.requireNonNull(query, "Map request is required.");
        if (query.structures() != null
                && !catalog.structures().stream()
                        .map(StructureEntry::id)
                        .toList()
                        .containsAll(query.structures()))
            throw new IllegalArgumentException(
                    "The map contains an unregistered original structure ID.");
        if (query.side() < 1 || query.side() > 256 || query.step() < 1 || query.step() > 524288)
            throw new IllegalArgumentException(
                    "Map sample dimensions exceed the bounded worker request.");
        validateBounds(query.minX(), query.minZ(), (long) query.side() * query.step());
        long started = System.nanoTime();
        var context = context(query.world(), query.seed());
        if (query.y() != null
                && (query.y() < context.height.getMinY()
                        || query.y() >= context.height.getMinY() + context.height.getHeight()))
            throw new IllegalArgumentException("The selected Y is outside this dimension.");
        int count = query.side() * query.side();
        int[] ids = new int[count], heights = new int[count], fluids = new int[count];
        String[] biomes = new String[count];
        if (mapSampler != null && query.y() == null && count >= 1024) {
            int middle = query.side() / 2;
            var pending =
                    mapSampler.submit(
                            () ->
                                    sampleMap(
                                            context(samplingContexts, query.world(), query.seed()),
                                            query,
                                            cancelled,
                                            middle,
                                            query.side(),
                                            biomes,
                                            heights,
                                            fluids));
            try {
                sampleMap(context, query, cancelled, 0, middle, biomes, heights, fluids);
                pending.get();
            } catch (InterruptedException error) {
                cancelled.set(true);
                Thread.currentThread().interrupt();
                throw new CancellationException();
            } catch (ExecutionException error) {
                throw new CompletionException(error.getCause());
            } finally {
                if (!pending.isDone()) {
                    cancelled.set(true);
                    try {
                        pending.get();
                    } catch (InterruptedException error) {
                        Thread.currentThread().interrupt();
                    } catch (ExecutionException error) {
                        /* Preserve the original failing branch. */
                    }
                }
            }
        } else {
            sampleMap(context, query, cancelled, 0, query.side(), biomes, heights, fluids);
        }
        // Publish row-major palette indices only after both private samplers have finished.
        var palette = new ArrayList<String>();
        var indices = new HashMap<String, Integer>();
        for (int index = 0; index < count; index++) {
            var biome = biomes[index];
            ids[index] =
                    indices.computeIfAbsent(
                            biome,
                            id -> {
                                palette.add(id);
                                return palette.size() - 1;
                            });
        }
        var markers =
                query.structures() != null && query.structures().isEmpty()
                        ? List.<Position>of()
                        : structures(
                                context,
                                query.minX(),
                                query.minZ(),
                                query.side() * query.step(),
                                query.side() * query.step(),
                                query.structures(),
                                cancelled);
        return new MapResult(
                Long.toString(context.seed),
                query.world(),
                query.minX(),
                query.minZ(),
                query.step(),
                query.side(),
                query.y(),
                palette,
                ids,
                heights,
                fluids,
                markers,
                elapsed(started));
    }

    private void sampleMap(
            Context context,
            MapQuery query,
            AtomicBoolean cancelled,
            int fromRow,
            int toRow,
            String[] biomes,
            int[] heights,
            int[] fluids) {
        for (int z = fromRow; z < toRow; z++)
            for (int x = 0; x < query.side(); x++) {
                check(cancelled);
                int wx = query.minX() + x * query.step(), wz = query.minZ() + z * query.step();
                var column =
                        query.y() == null && query.terrain()
                                ? context.generator.getBaseColumn(
                                        wx, wz, context.height, context.random)
                                : null;
                int y;
                if (column != null) {
                    y = context.height.getMinY() + context.height.getHeight() - 1;
                    var surface = Heightmap.Types.WORLD_SURFACE_WG.isOpaque();
                    while (y >= context.height.getMinY() && !surface.test(column.getBlock(y))) y--;
                } else {
                    y =
                            query.y() == null
                                    ? context.generator.getBaseHeight(
                                                    wx,
                                                    wz,
                                                    Heightmap.Types.WORLD_SURFACE_WG,
                                                    context.height,
                                                    context.random)
                                            - 1
                                    : query.y();
                }
                int ground = y, fluid = 0;
                if (column != null) {
                    var state = column.getBlock(y).getFluidState();
                    fluid =
                            state.isEmpty()
                                    ? 0
                                    : state.getType().isSame(Fluids.WATER)
                                            ? 1
                                            : state.getType().isSame(Fluids.LAVA) ? 2 : 3;
                    while (ground >= context.height.getMinY()
                            && !column.getBlock(ground).getFluidState().isEmpty()) ground--;
                }
                String biome =
                        biomeName(
                                context.biomes.getBiome(
                                        wx, Math.max(y, context.height.getMinY()), wz));
                int index = z * query.side() + x;
                biomes[index] = biome;
                heights[index] = ground;
                fluids[index] = fluid;
            }
    }

    StructureResult findStructures(StructureQuery query, AtomicBoolean cancelled) {
        deadline = 0;
        if (query == null
                || query.radius() < 0
                || query.radius() > 8192
                || query.limit() < 1
                || query.limit() > 256
                || query.centerX() < -30_000_000
                || query.centerX() >= 30_000_000
                || query.centerZ() < -30_000_000
                || query.centerZ() >= 30_000_000
                || query.targets() == null
                || query.targets().isEmpty()
                || query.targets().size() > 128
                || new HashSet<>(query.targets()).size() != query.targets().size())
            throw new IllegalArgumentException("Invalid structure query bounds or targets.");
        if (!catalog.structures().stream()
                .map(StructureEntry::id)
                .toList()
                .containsAll(query.targets()))
            throw new IllegalArgumentException(
                    "The query contains an unregistered original structure ID.");
        long started = System.nanoTime();
        var context = context(query.world(), query.seed());
        int minX = Math.max(-30_000_000, query.centerX() - query.radius());
        int minZ = Math.max(-30_000_000, query.centerZ() - query.radius());
        int maxX = Math.min(29_999_999, query.centerX() + query.radius());
        int maxZ = Math.min(29_999_999, query.centerZ() + query.radius());
        var positions =
                structures(
                                context,
                                minX,
                                minZ,
                                maxX - minX + 1,
                                maxZ - minZ + 1,
                                query.targets(),
                                cancelled)
                        .stream()
                        .filter(
                                position ->
                                        structureDistance(position, query)
                                                <= (long) query.radius() * query.radius())
                        .sorted(
                                Comparator.comparingLong(
                                                (Position position) ->
                                                        structureDistance(position, query))
                                        .thenComparing(Position::id)
                                        .thenComparingInt(Position::x)
                                        .thenComparingInt(Position::z)
                                        .thenComparingInt(Position::y))
                        .toList();
        check(cancelled);
        return new StructureResult(
                Long.toString(context.seed),
                query.world(),
                query.centerX(),
                query.centerZ(),
                query.radius(),
                query.targets(),
                positions.size(),
                positions.stream().limit(query.limit()).toList(),
                elapsed(started));
    }

    private static long structureDistance(Position position, StructureQuery query) {
        long x = (long) position.x() - query.centerX(), z = (long) position.z() - query.centerZ();
        return x * x + z * z;
    }

    private static void validateBounds(int x, int z, long span) {
        if (span < 1
                || x < -30_000_000
                || z < -30_000_000
                || x + span > 30_000_000
                || z + span > 30_000_000)
            throw new IllegalArgumentException(
                    "The requested area exceeds the original world coordinate range.");
    }

    private List<Position> structures(
            Context context,
            int minX,
            int minZ,
            int width,
            int depth,
            List<String> targets,
            AtomicBoolean cancelled) {
        var chunks = new LinkedHashSet<ChunkPos>();
        int minCx = Math.floorDiv(minX - 128, 16), minCz = Math.floorDiv(minZ - 128, 16);
        int maxCx = Math.floorDiv(minX + width + 128, 16),
                maxCz = Math.floorDiv(minZ + depth + 128, 16);
        for (var holder : context.structures.possibleStructureSets()) {
            if (targets != null
                    && holder.value().structures().stream()
                            .noneMatch(
                                    entry ->
                                            targets.contains(
                                                    entry.structure()
                                                            .unwrapKey()
                                                            .orElseThrow()
                                                            .identifier()
                                                            .getPath()))) continue;
            var placement = holder.value().placement();
            if (placement instanceof RandomSpreadStructurePlacement spread) {
                int spacing = spread.spacing();
                for (int z = Math.floorDiv(minCz, spacing); z <= Math.floorDiv(maxCz, spacing); z++)
                    for (int x = Math.floorDiv(minCx, spacing);
                            x <= Math.floorDiv(maxCx, spacing);
                            x++) {
                        check(cancelled);
                        var pos =
                                spread.getPotentialStructureChunk(
                                        context.seed, x * spacing, z * spacing);
                        if (pos.x() >= minCx
                                && pos.x() <= maxCx
                                && pos.z() >= minCz
                                && pos.z() <= maxCz
                                && placement.isStructureChunk(context.structures, pos.x(), pos.z()))
                            chunks.add(pos);
                    }
            } else if (placement instanceof ConcentricRingsStructurePlacement rings) {
                for (var pos : context.structures.getRingPositionsFor(rings))
                    if (pos.x() >= minCx
                            && pos.x() <= maxCx
                            && pos.z() >= minCz
                            && pos.z() <= maxCz) chunks.add(pos);
            } else {
                if ((long) (maxCx - minCx + 1) * (maxCz - minCz + 1) > 1_000_000)
                    throw new IllegalArgumentException(
                            "Zoom in before requesting dense original structure placement.");
                for (int z = minCz; z <= maxCz; z++)
                    for (int x = minCx; x <= maxCx; x++) {
                        check(cancelled);
                        if (placement.isStructureChunk(context.structures, x, z))
                            chunks.add(new ChunkPos(x, z));
                    }
            }
        }
        var result = new ArrayList<Position>();
        for (var chunk : chunks) {
            check(cancelled);
            for (var marker : starts(context, chunk)) {
                if (targets != null && !targets.contains(marker.id())) continue;
                if (marker.x() >= minX
                        && marker.x() < (long) minX + width
                        && marker.z() >= minZ
                        && marker.z() < (long) minZ + depth) result.add(marker);
            }
        }
        return result;
    }

    private List<Position> starts(Context context, ChunkPos pos) {
        var found = context.starts.get(pos);
        if (found != null) return found;
        var chunk = new ProtoChunk(pos, UpgradeData.EMPTY, context.height, containers, null);
        // Vanilla performs weighted entry choice, placement frequency/exclusions and full piece
        // assembly.
        context.generator.createStructures(
                registries,
                context.structures,
                context.manager,
                chunk,
                templates,
                ResourceKey.create(
                        Registries.DIMENSION,
                        Identifier.withDefaultNamespace(context.world.dimension())));
        var result = new ArrayList<Position>();
        for (var entry : chunk.getAllStarts().entrySet()) {
            var start = entry.getValue();
            if (!start.isValid()) continue;
            var box = start.getBoundingBox();
            var center = box.getCenter();
            result.add(
                    new Position(
                            registries
                                    .lookupOrThrow(Registries.STRUCTURE)
                                    .getKey(entry.getKey())
                                    .getPath(),
                            "structure",
                            center.getX(),
                            center.getY(),
                            center.getZ(),
                            null,
                            start.getPieces().size(),
                            new int[] {
                                box.minX(),
                                box.minY(),
                                box.minZ(),
                                box.maxX(),
                                box.maxY(),
                                box.maxZ()
                            }));
        }
        context.starts.put(pos, result);
        if (context.starts.size() > 4096)
            context.starts.remove(context.starts.keySet().iterator().next());
        return result;
    }

    SpawnResult spawn(SpawnQuery query, AtomicBoolean cancelled) throws Exception {
        deadline = 0;
        long started = System.nanoTime();
        var position = spawnPosition(query.world(), query.seed(), cancelled);
        return new SpawnResult(query.seed(), query.world(), position, elapsed(started));
    }

    private Position spawnPosition(World world, String seed, AtomicBoolean cancelled)
            throws Exception {
        if (!"overworld".equals(world.dimension()))
            throw new IllegalArgumentException("World spawn belongs to the Overworld.");
        context(world, seed); // Applies the same original preset and biome validation as every map
        // request.
        check(cancelled);
        String key = seed + "/" + world.preset() + "/" + world.biome();
        var position = spawns.get(key);
        if (position == null) {
            position = spawnWorker.calculate(new SpawnQuery(world, seed), () -> check(cancelled));
            check(cancelled);
            spawns.put(key, position);
            if (spawns.size() > 128) spawns.remove(spawns.keySet().iterator().next());
        }
        return position;
    }

    SearchResult search(SearchQuery query, AtomicBoolean cancelled, Consumer<SearchResult> progress)
            throws Exception {
        if (query == null
                || query.conditions() == null
                || query.conditions().isEmpty()
                || query.conditions().size() > 12
                || query.maxSeeds() < 1
                || query.maxSeeds() > 1_000_000
                || query.maxResults() < 1
                || query.maxResults() > 100
                || query.timeLimitSeconds() < 1
                || query.timeLimitSeconds() > 3600
                || query.offset() < 0
                || query.biomeStep() < 4
                || query.biomeStep() > 256)
            throw new IllegalArgumentException("Invalid bounded search request.");
        long startSeed = Long.parseLong(query.startSeed()),
                started = System.nanoTime(),
                tested = 0,
                lastProgress = started;
        var ids = new HashSet<String>(List.of("origin", "spawn"));
        for (var condition : query.conditions()) {
            if (condition.id() == null
                    || condition.id().isBlank()
                    || ids.contains(condition.id())
                    || condition.targets() == null
                    || condition.targets().isEmpty()
                    || condition.targets().size() > 128
                    || condition.minimum() < 1
                    || condition.minimum() > 64
                    || condition.radius() < 0
                    || condition.radius() > 8192
                    || !List.of("structure", "biome").contains(condition.kind())
                    || ("biome".equals(condition.kind()) && condition.minimum() != 1)
                    || (condition.reference() != null
                            && !"origin".equals(condition.reference())
                            && !ids.contains(condition.reference())))
                throw new IllegalArgumentException(
                        "Invalid condition identity, reference, kind or bounds.");
            ids.add(condition.id());
            if ("spawn".equals(condition.reference())
                    && !"overworld".equals(query.world().dimension()))
                throw new IllegalArgumentException("World spawn references require the Overworld.");
            var legal =
                    "biome".equals(condition.kind())
                            ? catalog.biomes().stream().map(BiomeEntry::id).toList()
                            : catalog.structures().stream().map(StructureEntry::id).toList();
            if (!legal.containsAll(condition.targets()))
                throw new IllegalArgumentException(
                        "A condition contains an unregistered original ID.");
        }
        var hits = new ArrayList<SearchHit>();
        String status = "budget_exhausted";
        deadline = started + query.timeLimitSeconds() * 1_000_000_000L;
        for (; tested < query.maxSeeds(); tested++) {
            if (cancelled.get()) {
                status = "cancelled";
                break;
            }
            if (elapsed(started) >= query.timeLimitSeconds() * 1000.0) {
                status = "time_limit";
                break;
            }
            String seed = Long.toString(startSeed + query.offset() + tested);
            var context = context(query.world(), seed);
            var matches = new Match[query.conditions().size()];
            try {
                if (solve(context, query, matches, cancelled))
                    hits.add(new SearchHit(seed, List.of(matches)));
            } catch (CancellationException exception) {
                status = cancelled.get() ? "cancelled" : "time_limit";
                break;
            }
            if (hits.size() >= query.maxResults()) {
                tested++;
                status = "completed";
                break;
            }
            if (System.nanoTime() - lastProgress >= 250_000_000L) {
                progress.accept(
                        new SearchResult(
                                "running",
                                tested + 1,
                                query.offset() + tested + 1,
                                List.copyOf(hits),
                                elapsed(started)));
                lastProgress = System.nanoTime();
            }
        }
        deadline = 0;
        return new SearchResult(
                status, tested, query.offset() + tested, List.copyOf(hits), elapsed(started));
    }

    private boolean solve(
            Context context,
            SearchQuery query,
            Match[] matches,
            AtomicBoolean cancelled)
            throws Exception {
        // Each condition has one parent. Independent trees never need to retry each other's
        // witnesses; only a descendant can require a different anchor in its own tree.
        for (int index = 0; index < query.conditions().size(); index++) {
            check(cancelled);
            var source = query.conditions().get(index).reference();
            if (source != null && !"origin".equals(source) && !"spawn".equals(source)) continue;
            var reference = "spawn".equals(source)
                    ? spawnPosition(query.world(), Long.toString(context.seed), cancelled)
                    : new Position("origin", "origin", 0, 0, 0, null, 0, null);
            if (!solveCondition(context, query, index, reference, matches, cancelled)) return false;
        }
        return true;
    }

    private boolean solveCondition(
            Context context,
            SearchQuery query,
            int index,
            Position reference,
            Match[] matches,
            AtomicBoolean cancelled)
            throws Exception {
        check(cancelled);
        var condition = query.conditions().get(index);
        var children = new ArrayList<Integer>();
        for (int child = index + 1; child < query.conditions().size(); child++)
            if (condition.id().equals(query.conditions().get(child).reference())) children.add(child);
        List<Position> positions;
        if ("structure".equals(condition.kind())) {
            int radius = condition.radius();
            positions =
                    structures(
                                    context,
                                    reference.x() - radius,
                                    reference.z() - radius,
                                    radius * 2 + 1,
                                    radius * 2 + 1,
                                    condition.targets(),
                                    cancelled)
                            .stream()
                            .filter(p -> distance(p, reference) <= radius)
                            .toList();
        } else
            positions = biomePositions(context, reference, condition, query.biomeStep(), children.isEmpty() ? 1 : 64, cancelled);
        if (positions.size() < condition.minimum()) return false;
        for (var anchor : positions) {
            check(cancelled);
            var witnesses = new ArrayList<Position>();
            witnesses.add(anchor);
            for (var witness : positions) {
                if (!witness.equals(anchor)) witnesses.add(witness);
                if (witnesses.size() == 64) break;
            }
            boolean accepted = true;
            for (int child : children)
                if (!solveCondition(context, query, child, anchor, matches, cancelled)) {
                    accepted = false;
                    break;
                }
            if (accepted) {
                matches[index] = new Match(condition.id(), anchor, List.copyOf(witnesses));
                return true;
            }
        }
        return false;
    }

    private List<Position> biomePositions(
            Context context,
            Position reference,
            Condition condition,
            int step,
            int limit,
            AtomicBoolean cancelled) {
        var found = new ArrayList<Position>();
        // Positive witnesses only. A sampled miss never establishes absence of a biome.
        int radius = condition.radius();
        for (int z = -radius; z <= radius; z += step)
            for (int x = -radius; x <= radius; x += step) {
                check(cancelled);
                if (Math.hypot(x, z) > radius) continue;
                int wx = reference.x() + x, wz = reference.z() + z;
                int y =
                        condition.y() == null
                                ? ("overworld".equals(context.world.dimension()) ? 128 : 64)
                                : condition.y();
                if (y < context.height.getMinY()
                        || y >= context.height.getMinY() + context.height.getHeight())
                    throw new IllegalArgumentException("Condition Y is outside this dimension.");
                String biome = biomeName(context.biomes.getBiome(wx, y, wz));
                if (!condition.targets().contains(biome)) continue;
                if (condition.y() == null) {
                    y =
                            context.generator.getBaseHeight(
                                            wx,
                                            wz,
                                            Heightmap.Types.WORLD_SURFACE_WG,
                                            context.height,
                                            context.random)
                                    - 1;
                    biome = biomeName(context.biomes.getBiome(wx, y, wz));
                    if (!condition.targets().contains(biome)) continue;
                }
                found.add(new Position(biome, "biome", wx, y, wz, biome, 0, null));
                if (found.size() >= limit) return found;
            }
        return found;
    }

    private static double distance(Position a, Position b) {
        return Math.hypot((double) a.x() - b.x(), (double) a.z() - b.z());
    }

    private static double elapsed(long started) {
        return (System.nanoTime() - started) / 1_000_000.0;
    }

    @Override
    public void close() throws Exception {
        spawnWorker.close();
        if (mapSampler != null) {
            mapSampler.shutdown();
            if (!mapSampler.awaitTermination(10, TimeUnit.SECONDS)) mapSampler.shutdownNow();
        }
        resources.close();
        storage.close();
    }

    private record Context(
            World world,
            long seed,
            BiomeSource source,
            NoiseBasedChunkGenerator generator,
            RandomState random,
            BiomeManager biomes,
            LevelHeightAccessor height,
            ChunkGeneratorStructureState structures,
            StructureManager manager,
            LinkedHashMap<ChunkPos, List<Position>> starts) {
        Context(
                World world,
                long seed,
                BiomeSource source,
                NoiseBasedChunkGenerator generator,
                RandomState random,
                BiomeManager biomes,
                LevelHeightAccessor height,
                ChunkGeneratorStructureState structures,
                StructureManager manager) {
            this(
                    world,
                    seed,
                    source,
                    generator,
                    random,
                    biomes,
                    height,
                    structures,
                    manager,
                    new LinkedHashMap<>(128, .75f, true));
        }
    }
}
