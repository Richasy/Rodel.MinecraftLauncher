package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Portable original-JVM entry point. Original APIs own generation; the host owns the disposable
 * workspace.
 */
public final class OriginalSeedWorker {
    private final NativeSeedSymbols symbols;
    private final NativeAncientSymbols ancient;
    private final NativeMappedSymbols mapped;
    private final NativePreRegistrySymbols unnamed;
    private final PrintWriter output;
    private final String version;
    private final File root;
    private final Map<String, AtomicBoolean> pending = new ConcurrentHashMap<>();
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private NativeSeedContext current;
    private String worldKey;
    private int number;

    private OriginalSeedWorker(
            String jar, String version, File root, String mappings, PrintStream protocol)
            throws Exception {
        NativeMappedSymbols candidate =
                new NativeMappedSymbols(mappings.isEmpty() ? null : Paths.get(mappings));
        unnamed = NativePreRegistrySymbols.tryDiscover(candidate, jar);
        mapped =
                unnamed != null
                                || candidate.hasType(
                                        "net.minecraft.world.level.levelgen.WorldGenSettings")
                                || candidate.hasType("net.minecraft.server.WorldLoader")
                                || candidate.hasType("net.minecraft.server.level.ServerLevel")
                                        && candidate.hasType(
                                                "net.minecraft.world.level.LevelSettings")
                        ? candidate
                        : null;
        ancient = mapped == null ? NativeAncientSymbols.tryOpen(jar) : null;
        symbols = mapped == null && ancient == null ? new NativeSeedSymbols(jar) : null;
        if (symbols != null) symbols.initialize();
        else if (mapped != null && unnamed == null) {
            try {
                mapped.call(mapped.type("net.minecraft.SharedConstants"), "tryDetectVersion");
            } catch (NoSuchMethodException older) {
                mapped.call(mapped.type("net.minecraft.SharedConstants"), "getCurrentVersion");
            }
            mapped.call(mapped.type("net.minecraft.server.Bootstrap"), "bootStrap");
        }
        this.version = version;
        this.root = root.getCanonicalFile();
        output = new PrintWriter(new OutputStreamWriter(protocol, StandardCharsets.UTF_8), true);
    }

    /**
     * Supplies deterministic, non-network defaults only to abstract host methods; game data comes
     * from native WorldSettings.
     */
    public static Object defaultValue(Class<?> type) {
        if (type == String.class) return "";
        if (type.isEnum())
            for (Object value : type.getEnumConstants())
                if (((Enum<?>) value).name().equals("CREATIVE")
                        || ((Enum<?>) value).name().equals("PEACEFUL")) return value;
        Object logger = NativeAncientLog.create(type);
        if (logger != null) return logger;
        throw new IllegalStateException("SEED_HOST_DEFAULT_UNAVAILABLE: " + type.getName());
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> object(Map<String, Object> value, String key) {
        Object result = value.get(key);
        if (!(result instanceof Map))
            throw new IllegalArgumentException("Missing request object: " + key);
        return (Map<String, Object>) result;
    }

    private static int integer(Map<String, Object> value, String key) {
        return ((Number) value.get(key)).intValue();
    }

    NativeSeedContext world(Map<String, Object> request) throws Exception {
        Map<String, Object> config = object(request, "world");
        long seed = Long.parseLong(Json.string(request, "seed"));
        String preset = Json.string(config, "preset"), dimension = Json.string(config, "dimension");
        String nativePreset =
                preset.equals("normal")
                        ? "default"
                        : preset.equals("large_biomes") ? "largeBiomes" : preset;
        int id =
                dimension.equals("overworld")
                        ? 0
                        : dimension.equals("the_nether")
                                ? -1
                                : dimension.equals("the_end") ? 1 : Integer.MIN_VALUE;
        if (id == Integer.MIN_VALUE && symbols != null)
            throw new IllegalArgumentException("Unknown original dimension.");
        String key = seed + "/" + Json.encode(config);
        if (!key.equals(worldKey)) {
            NativeSeedContext previous = current;
            current = null;
            worldKey = null;
            if (previous != null) previous.close();
            current =
                    unnamed != null
                            ? new NativePreRegistryLegacyWorld(
                                    unnamed, new File(root, "world-" + (++number)), seed, config)
                            : mapped != null
                                    ? mapped.hasType("net.minecraft.server.WorldLoader")
                                            ? new NativeMappedWorld(
                                                    mapped,
                                                    new File(root, "world-" + (++number)),
                                                    seed,
                                                    config)
                                            : mapped.hasType(
                                                            "net.minecraft.world.level.levelgen.WorldGenSettings")
                                                    ? new NativeRegistryWorld(
                                                            mapped,
                                                            new File(root, "world-" + (++number)),
                                                            seed,
                                                            config)
                                                    : new NativePreRegistryWorld(
                                                            mapped,
                                                            new File(root, "world-" + (++number)),
                                                            seed,
                                                            config)
                                    : ancient != null
                                            ? new NativeAncientWorld(
                                                    ancient,
                                                    new File(root, "world-" + (++number)),
                                                    seed,
                                                    preset,
                                                    dimension)
                                            : new NativeSeedWorld(
                                                    symbols,
                                                    new File(root, "world-" + (++number)),
                                                    seed,
                                                    nativePreset,
                                                    id,
                                                    config.get("biome") instanceof String
                                                            ? (String) config.get("biome")
                                                            : null);
            worldKey = key;
        }
        return current;
    }

    private Object catalog() throws Exception {
        NativeSeedContext sample =
                world(
                        Json.object(
                                "seed",
                                "0",
                                "world",
                                Json.object("preset", "normal", "dimension", "overworld")));
        List<String> ids = new ArrayList<>(new TreeSet<>(sample.biomeIds.values()));
        Map<String, List<String>> dimensions = new LinkedHashMap<>(),
                structures = new LinkedHashMap<>();
        Map<String, Set<String>> eligibility = new HashMap<>();
        List<Object> sources = new ArrayList<>();
        for (String dimension : sample.dimensions()) {
            NativeSeedContext nativeWorld =
                    world(
                            Json.object(
                                    "seed",
                                    "0",
                                    "world",
                                    Json.object("preset", "normal", "dimension", dimension)));
            List<String> allowed = nativeWorld.possibleBiomes();
            dimensions.put(dimension, allowed);
            sources.add(
                    Json.object(
                            "id",
                            dimension,
                            "minY",
                            nativeWorld.minimumY,
                            "height",
                            nativeWorld.heightLimit - nativeWorld.minimumY,
                            "biomes",
                            allowed));
            for (String id : nativeWorld.structures.ids()) {
                structures.computeIfAbsent(id, key -> new ArrayList<>()).add(dimension);
                eligibility
                        .computeIfAbsent(id, key -> new TreeSet<>())
                        .addAll(nativeWorld.structures.biomes(id));
            }
        }
        List<Object> biomes = new ArrayList<>(), structureEntries = new ArrayList<>();
        for (String id : ids) {
            List<String> allowed = new ArrayList<>();
            for (Map.Entry<String, List<String>> dimension : dimensions.entrySet())
                if (dimension.getValue().contains(id)) allowed.add(dimension.getKey());
            biomes.add(Json.object("id", id, "dimensions", allowed));
        }
        for (Map.Entry<String, List<String>> entry : structures.entrySet())
            structureEntries.add(
                    Json.object(
                            "id",
                            entry.getKey(),
                            "dimensions",
                            entry.getValue(),
                            "biomes",
                            eligibility.get(entry.getKey())));
        return Json.object(
                "version",
                version,
                "biomes",
                biomes,
                "structures",
                structureEntries,
                "dimensions",
                sources,
                "blockDefaults",
                Collections.emptyMap(),
                "names",
                sample.biomeNames,
                "seedBoundSpawn",
                sample.seedBoundSpawn());
    }

    private Object map(Map<String, Object> query, AtomicBoolean cancelled) throws Exception {
        NativeSeedContext original = world(query);
        int side = integer(query, "side"),
                step = integer(query, "step"),
                minX = integer(query, "minX"),
                minZ = integer(query, "minZ");
        if (side < 1
                || side > 256
                || step < 1
                || step > 524288
                || Math.abs((long) minX) > 30000000
                || Math.abs((long) minZ) > 30000000
                || (long) minX + (long) side * step > 30000000
                || (long) minZ + (long) side * step > 30000000)
            throw new IllegalArgumentException("SEED_MAP_BUDGET");
        Number slice = (Number) query.get("y");
        List<String> palette = new ArrayList<>();
        Map<String, Integer> indices = new LinkedHashMap<>();
        List<Integer> biomes = new ArrayList<>(),
                heights = new ArrayList<>(),
                fluids = new ArrayList<>();
        boolean hasBiomes = !original.biomeIds.isEmpty();
        List<Integer> colors = new ArrayList<>();
        List<Boolean> known = new ArrayList<>();
        long start = System.nanoTime();
        for (int z = 0; z < side; z++)
            for (int x = 0; x < side; x++) {
                if (cancelled.get()) throw new CancellationException();
                int wx = minX + x * step, wz = minZ + z * step;
                boolean available = original.known(wx, wz);
                known.add(available);
                if (!available) {
                    biomes.add(-1);
                    heights.add(original.minimumY - 1);
                    fluids.add(0);
                    if (!hasBiomes) colors.add(0);
                    continue;
                }
                if (slice != null
                        && (slice.intValue() < original.minimumY
                                || slice.intValue() >= original.heightLimit))
                    throw new IllegalArgumentException("Y is outside this native dimension.");
                int[] column =
                        original.column(
                                wx,
                                wz,
                                slice == null ? null : slice.intValue(),
                                Boolean.TRUE.equals(query.get("terrain")));
                int y = column[0];
                if (!hasBiomes) {
                    biomes.add(-1);
                    heights.add(y);
                    fluids.add(column[1]);
                    colors.add(original.surfaceColor(wx, y, wz));
                    continue;
                }
                String biome =
                        original.biome(
                                wx,
                                Math.max(original.minimumY, column.length > 2 ? column[2] : y),
                                wz);
                Integer index = indices.get(biome);
                if (index == null) {
                    index = palette.size();
                    palette.add(biome);
                    indices.put(biome, index);
                }
                biomes.add(index);
                heights.add(y);
                fluids.add(column[1]);
            }
        @SuppressWarnings("unchecked")
        List<String> targets = (List<String>) query.get("structures");
        List<Map<String, Object>> structures =
                targets != null && targets.isEmpty()
                        ? Collections.emptyList()
                        : original.structures.find(
                                minX,
                                minZ,
                                minX + side * step - 1,
                                minZ + side * step - 1,
                                targets,
                                cancelled);
        return Json.object(
                "seed",
                query.get("seed"),
                "world",
                query.get("world"),
                "minX",
                minX,
                "minZ",
                minZ,
                "side",
                side,
                "step",
                step,
                "y",
                slice,
                "dimensionMinY",
                original.minimumY,
                "surfaceColors",
                colors,
                "known",
                known.contains(Boolean.FALSE) ? known : Collections.emptyList(),
                "palette",
                palette,
                "biomes",
                biomes,
                "heights",
                heights,
                "fluids",
                fluids,
                "structures",
                structures,
                "elapsedMilliseconds",
                (System.nanoTime() - start) / 1000000.0);
    }

    private Object structures(Map<String, Object> query, AtomicBoolean cancelled) throws Exception {
        NativeSeedContext original = world(query);
        int x = integer(query, "centerX"),
                z = integer(query, "centerZ"),
                radius = integer(query, "radius"),
                limit = integer(query, "limit");
        if (radius < 0 || radius > 8192 || limit < 1 || limit > 256)
            throw new IllegalArgumentException("SEED_STRUCTURE_BUDGET");
        @SuppressWarnings("unchecked")
        List<String> targets = (List<String>) query.get("targets");
        if (targets == null || targets.isEmpty() || !original.structures.ids().containsAll(targets))
            throw new IllegalArgumentException("Unregistered native structure ID.");
        long start = System.nanoTime();
        List<Map<String, Object>> positions =
                original.structures.find(
                        x - radius, z - radius, x + radius, z + radius, targets, cancelled);
        positions.removeIf(value -> distance(value, x, z) > (long) radius * radius);
        positions.sort(
                Comparator.<Map<String, Object>>comparingLong(value -> distance(value, x, z))
                        .thenComparing(value -> (String) value.get("id"))
                        .thenComparingInt(value -> ((Number) value.get("x")).intValue())
                        .thenComparingInt(value -> ((Number) value.get("z")).intValue()));
        return Json.object(
                "seed",
                query.get("seed"),
                "world",
                query.get("world"),
                "centerX",
                x,
                "centerZ",
                z,
                "radius",
                radius,
                "targets",
                targets,
                "found",
                positions.size(),
                "positions",
                new ArrayList<>(positions.subList(0, Math.min(limit, positions.size()))),
                "elapsedMilliseconds",
                (System.nanoTime() - start) / 1000000.0);
    }

    private static long distance(Map<String, Object> value, int x, int z) {
        long dx = ((Number) value.get("x")).longValue() - x,
                dz = ((Number) value.get("z")).longValue() - z;
        return dx * dx + dz * dz;
    }

    private Object generate(
            Map<String, Object> query,
            AtomicBoolean cancelled,
            java.util.function.Consumer<Map<String, Object>> progress)
            throws Exception {
        worldKey = null;
        progress.accept(Json.object("phase", "loading", "completed", 0, "total", 0));
        NativeSeedContext original = world(query);
        original.progress = progress;
        Map<String, Object> bounds = object(query, "bounds");
        File result =
                original.save(
                        integer(bounds, "minX"),
                        integer(bounds, "minZ"),
                        integer(bounds, "maxX"),
                        integer(bounds, "maxZ"),
                        Boolean.TRUE.equals(query.get("includeLoot")),
                        cancelled);
        Map<String, Object> region;
        try {
            region = original.readRegion(query, cancelled);
        } finally {
            try {
                original.close();
            } finally {
                current = null;
                worldKey = null;
            }
        }
        Path source = result.toPath(), target = new File(root, "snapshot-" + (++number)).toPath();
        try (java.util.stream.Stream<Path> files = Files.walk(source)) {
            Iterator<Path> iterator = files.iterator();
            int count = 0;
            while (iterator.hasNext()) {
                if (cancelled.get()) throw new CancellationException();
                if (++count > 10000) throw new IOException("SEED_SNAPSHOT_BUDGET");
                Path file = iterator.next(), destination = target.resolve(source.relativize(file));
                if (Files.isSymbolicLink(file)) throw new IOException("SEED_SNAPSHOT_LINK");
                if (Files.isDirectory(file)) Files.createDirectories(destination);
                else Files.copy(file, destination);
            }
        }
        return Json.object(
                "directory",
                target.toFile().getCanonicalPath(),
                "region",
                region,
                "inventories",
                original.inventories);
    }

    private Object spawn(Map<String, Object> query) throws Exception {
        if (!"overworld".equals(object(query, "world").get("dimension")))
            throw new IllegalArgumentException("Native shared spawn belongs to the Overworld.");
        long started = System.nanoTime();
        NativeSeedContext original = world(query);
        if (!original.seedBoundSpawn())
            throw new IllegalArgumentException(
                    "SEED_SPAWN_NOT_SEED_BOUND: This original game uses additional randomness when"
                            + " selecting spawn; inspect the saved world's actual spawn instead.");
        Map<String, Object> position = original.spawn();
        return Json.object(
                "seed",
                query.get("seed"),
                "world",
                query.get("world"),
                "position",
                position,
                "elapsedMilliseconds",
                (System.nanoTime() - started) / 1000000.0);
    }

    private Object seed(Map<String, Object> request) {
        String text = (String) request.get("seed");
        if (text == null || text.isEmpty())
            return Json.object("seed", Long.toString(new Random().nextLong()));
        try {
            return Json.object("seed", Long.toString(Long.parseLong(text)));
        } catch (NumberFormatException symbolic) {
            return Json.object("seed", Long.toString(text.hashCode()));
        }
    }

    private synchronized void send(Object id, String kind, Object data, String error) {
        output.println(
                Json.encode(Json.object("id", id, "kind", kind, "data", data, "error", error)));
    }

    private void run() throws Exception {
        send(null, "ready", Json.object("protocol", 1, "version", version), null);
        try (BufferedReader input =
                new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = input.readLine()) != null) {
                if (line.length() > 1048576) throw new IOException("SEED_REQUEST_BUDGET");
                final Map<String, Object> request = Json.parse(line);
                String operation = Json.string(request, "operation");
                if (operation.equals("cancel")) {
                    AtomicBoolean flag = pending.get(request.get("cancelId"));
                    if (flag != null) flag.set(true);
                    continue;
                }
                final String id = Json.string(request, "id");
                if (id.length() > 80 || pending.size() >= 4)
                    throw new IOException("SEED_QUEUE_BUDGET");
                final AtomicBoolean cancelled = new AtomicBoolean();
                if (pending.putIfAbsent(id, cancelled) != null)
                    throw new IOException("SEED_DUPLICATE_REQUEST");
                executor.submit(
                        () -> {
                            try {
                                Object result;
                                switch (Json.string(request, "operation")) {
                                    case "catalog":
                                        result = catalog();
                                        break;
                                    case "map":
                                        result = map(object(request, "map"), cancelled);
                                        break;
                                    case "structures":
                                        result =
                                                structures(
                                                        object(request, "structures"), cancelled);
                                        break;
                                    case "spawn":
                                        result = spawn(object(request, "spawn"));
                                        break;
                                    case "seed":
                                        result = seed(request);
                                        break;
                                    case "loot":
                                        Map<String, Object> loot = object(request, "loot");
                                        result = world(loot).predictLoot(loot, cancelled);
                                        break;
                                    case "search":
                                        result =
                                                NativeSeedSearch.run(
                                                        this,
                                                        object(request, "search"),
                                                        cancelled,
                                                        snapshot ->
                                                                send(
                                                                        id,
                                                                        "progress",
                                                                        snapshot,
                                                                        null));
                                        break;
                                    case "generate":
                                        result =
                                                generate(
                                                        object(request, "region"),
                                                        cancelled,
                                                        snapshot ->
                                                                send(
                                                                        id,
                                                                        "progress",
                                                                        snapshot,
                                                                        null));
                                        break;
                                    default:
                                        throw new IllegalArgumentException(
                                                "SEED_OPERATION_UNAVAILABLE");
                                }
                                send(id, "result", result, null);
                            } catch (Throwable error) {
                                while (error instanceof InvocationTargetException
                                        && error.getCause() != null) error = error.getCause();
                                if (error instanceof CancellationException)
                                    send(id, "cancelled", null, null);
                                else {
                                    error.printStackTrace(System.err);
                                    String message = error.getMessage();
                                    send(
                                            id,
                                            message != null
                                                            && message.startsWith(
                                                                    "SEED_WORLD_UNSUPPORTED:")
                                                    ? "world_unsupported"
                                                    : "error",
                                            null,
                                            error.toString());
                                }
                            } finally {
                                pending.remove(id);
                            }
                        });
            }
        } finally {
            for (AtomicBoolean flag : pending.values()) flag.set(true);
            executor.shutdown();
            if (!executor.awaitTermination(5, TimeUnit.SECONDS)) executor.shutdownNow();
            if (current != null) current.close();
            if (symbols != null) symbols.close();
            if (ancient != null) ancient.close();
        }
    }

    /**
     * Runs with only the exact installed original classpath and a newly owned workspace. Closing
     * the host pipe ends this process.
     */
    public static void main(String[] args) throws Exception {
        PrintStream protocol = System.out;
        System.setOut(new PrintStream(System.err, true, "UTF-8"));
        if (args.length != 3 && args.length != 4)
            throw new IllegalArgumentException(
                    "Expected original client, version and isolated workspace.");
        try {
            new OriginalSeedWorker(
                            args[0],
                            args[1],
                            new File(args[2]),
                            args.length == 4 ? args[3] : "",
                            protocol)
                    .run();
        } catch (Throwable error) {
            error.printStackTrace(System.err);
            if (error.getMessage() != null
                    && error.getMessage().startsWith("SEED_ORIGINAL_SEED_UNAVAILABLE")) {
                protocol.println(
                        Json.encode(
                                Json.object(
                                        "id",
                                        null,
                                        "kind",
                                        "unavailable",
                                        "data",
                                        null,
                                        "error",
                                        error.getMessage())));
                protocol.flush();
            }
            System.exit(1);
        }
        System.exit(0);
    }
}
