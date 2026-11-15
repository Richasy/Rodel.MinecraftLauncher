package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.reflect.*;
import java.text.SimpleDateFormat;
import java.util.*;

/**
 * Captures immutable scalar observations on the native client loop; it never creates chunks,
 * changes a world, or controls input.
 */
final class LegacyObserver implements Observer {
    private final LegacySymbols symbols;
    private final String session, incarnation, gameDirectory;
    private final Map<Class<?>, EntityShape> shapes = new HashMap<Class<?>, EntityShape>();
    private final Set<Class<?>> nonEntities = new HashSet<Class<?>>();
    private volatile Map<String, Object> sample;
    private volatile long sampledAt;
    private Thread gameThread;
    private Object previousWorld, previousPlayer, previousServer;
    private long epoch, localEpoch, sequence, nextDirectoryProbe, nextSample;
    private String directory;

    LegacyObserver(LegacySymbols symbols, String session, String incarnation) throws IOException {
        this.symbols = symbols;
        this.session = session;
        this.incarnation = incarnation;
        gameDirectory = new File(symbols.bindings.getProperty("gameDirectory")).getCanonicalPath();
    }

    @Override
    public void update(Observable unused, Object client) {
        accept(client);
    }

    /** Receives only the native-loop callback installed through the bootstrap observer. */
    public void accept(Object client) {
        if (gameThread != Thread.currentThread()) {
            if (gameThread != null && gameThread.isAlive()) return;
            Class<?> nativeClient = client.getClass();
            while (nativeClient != null
                    && !nativeClient.getName().replace('.', '/').equals(symbols.client))
                nativeClient = nativeClient.getSuperclass();
            if (nativeClient == null) return;
            boolean nativeLoop = false;
            if (client instanceof Runnable) {
                for (StackTraceElement frame : Thread.currentThread().getStackTrace())
                    if (frame.getClassName().equals(nativeClient.getName())
                            && frame.getMethodName().equals("run")) nativeLoop = true;
            } else if (symbols.clientThread != null) {
                try {
                    Field field = nativeClient.getDeclaredField(symbols.clientThread);
                    field.setAccessible(true);
                    nativeLoop = field.get(client) == Thread.currentThread();
                } catch (Exception unavailable) {
                    return;
                }
            }
            // Instrumentation itself creates two client frames even for a background utility; frame
            // count
            // is not thread proof.
            if (!nativeLoop) return;
            gameThread = Thread.currentThread();
        }
        long now = System.nanoTime();
        if (nextSample != 0 && now < nextSample) return;
        nextSample = now + 100000000L;
        try {
            capture(client);
        } catch (Throwable unsupported) {
            unavailable();
        }
    }

    private void unavailable() {
        previousWorld = null;
        previousPlayer = null;
        previousServer = null;
        directory = null;
        nextDirectoryProbe = 0;
        sample = null;
        sampledAt = 0;
    }

    private void capture(Object client) throws Exception {
        Object player = symbols.field(client, "clientPlayer");
        EntityShape shape = player == null ? null : entity(player.getClass());
        if (player == null) {
            int best = -1;
            boolean ambiguous = false;
            for (Field field : fields(client.getClass())) {
                if (field.getType().isPrimitive() || field.getType().getClassLoader() == null)
                    continue;
                EntityShape candidate = entity(field.getType());
                if (candidate == null) continue;
                Object value = field.get(client);
                if (value == null) continue;
                int depth = depth(field.getType());
                if (depth > best) {
                    best = depth;
                    player = value;
                    shape = candidate;
                    ambiguous = false;
                } else if (depth == best && value != player) ambiguous = true;
            }
            if (ambiguous) throw new IllegalStateException("GAME_PLAYER_AMBIGUOUS");
        }
        if (player == null || shape == null) {
            unavailable();
            return;
        }
        Object world = symbols.field(client, "clientLevel");
        Object shared = sharedWorld(client, player, shape.root);
        if (world != null && world != shared) {
            unavailable();
            return;
        }
        world = shared;
        if (world == null) {
            unavailable();
            return;
        }
        Object server = symbols.call(client, "clientServer");
        if (server == null) server = findServer(client, world.getClass());
        if (previousWorld != world || previousPlayer != player) {
            epoch++;
            directory = null;
            nextDirectoryProbe = 0;
        }
        if (previousServer != server || server == null && previousWorld != world) {
            localEpoch++;
            directory = null;
            nextDirectoryProbe = 0;
        }
        previousWorld = world;
        previousPlayer = player;
        previousServer = server;
        long now = System.nanoTime();
        if (directory == null && now >= nextDirectoryProbe) {
            directory = findDirectory(server == null ? world : server, shape.root, client);
            nextDirectoryProbe = now + 1000000000L;
        }
        String dimension = dimension(player, world, shape);
        if (dimension == null) throw new IllegalStateException("GAME_DIMENSION_UNAVAILABLE");
        double x = coordinate(player, shape.x, "entityX"),
                y = coordinate(player, shape.y, "entityY"),
                z = coordinate(player, shape.z, "entityZ");
        if (!finite(x) || !finite(y) || !finite(z))
            throw new IllegalStateException("GAME_DATA_INVALID");
        Object id = symbols.call(player, "entityUuid");
        if (id == null) {
            List<Field> ids = new ArrayList<Field>();
            for (Field field : fields(shape.root))
                if (field.getType() == UUID.class) ids.add(field);
            if (ids.size() == 1) id = ids.get(0).get(player);
        }
        Float yaw = null;
        Object angle = symbols.call(player, "entityYaw");
        if (angle == null) angle = symbols.field(player, "entityYawField");
        if (angle instanceof Number && finite(((Number) angle).floatValue()))
            yaw = ((Number) angle).floatValue();
        Boolean channelLocal = localChannel(player, shape.root, world);
        boolean local =
                directory != null
                        && !Boolean.FALSE.equals(channelLocal)
                        && (!symbols.netty || Boolean.TRUE.equals(channelLocal));
        Map<String, Object> value =
                Json.object(
                        "sessionId",
                        session,
                        "gameIncarnation",
                        incarnation,
                        "sequence",
                        Long.toString(++sequence),
                        "sampledAt",
                        timestamp(),
                        "ready",
                        true,
                        "mode",
                        local ? "local_owned" : "remote_read_only",
                        "worldEpoch",
                        Long.toString(epoch),
                        "localWorldEpoch",
                        local ? Long.toString(localEpoch) : null,
                        "dimension",
                        dimension,
                        "playerId",
                        id instanceof UUID ? id.toString() : "local",
                        "x",
                        x,
                        "y",
                        y,
                        "z",
                        z,
                        "yaw",
                        yaw,
                        "clientThread",
                        Thread.currentThread().getName());
        // World path is retained in the same frozen sample so a subsequent query cannot mix epochs.
        if (local) value.put("savedDirectory", directory);
        sample = Collections.unmodifiableMap(value);
        sampledAt = now;
    }

    Map<String, Object> snapshot() {
        Map<String, Object> current = fresh();
        if (current == null)
            return Json.object(
                    "sessionId",
                    session,
                    "gameIncarnation",
                    incarnation,
                    "sequence",
                    Long.toString(sequence),
                    "sampledAt",
                    timestamp(),
                    "ready",
                    false,
                    "mode",
                    "unavailable",
                    "errorCode",
                    "GAME_WORLD_NOT_READY");
        Map<String, Object> result = new LinkedHashMap<String, Object>(current);
        result.remove("savedDirectory");
        return result;
    }

    Map<String, Object> query(Map<String, Object> request) {
        Map<String, Object> current = fresh();
        if (current == null) throw new IllegalArgumentException("GAME_WORLD_NOT_READY");
        if (!incarnation.equals(request.get("gameIncarnation"))
                || !current.get("worldEpoch").equals(request.get("worldEpoch"))
                || !current.get("dimension").equals(request.get("dimension")))
            throw new IllegalArgumentException("GAME_WORLD_STALE");
        if (!"world".equals(request.get("kind")))
            throw new IllegalArgumentException("GAME_QUERY_UNSUPPORTED");
        return Json.object(
                "sessionId",
                session,
                "gameIncarnation",
                incarnation,
                "worldEpoch",
                current.get("worldEpoch"),
                "dimension",
                current.get("dimension"),
                "sampledAt",
                current.get("sampledAt"),
                "kind",
                "world",
                "source",
                "client_synchronized",
                "data",
                Json.object(
                        "directory",
                        current.get("savedDirectory"),
                        "localWorldEpoch",
                        current.get("localWorldEpoch"),
                        "serverDataAvailable",
                        false));
    }

    private Map<String, Object> fresh() {
        Map<String, Object> value = sample;
        return value != null && System.nanoTime() - sampledAt < 1000000000L ? value : null;
    }

    private double coordinate(Object player, Field field, String role) throws Exception {
        Object value = field == null ? symbols.call(player, role) : field.get(player);
        if (!(value instanceof Number))
            throw new IllegalStateException("GAME_POSITION_UNAVAILABLE");
        return ((Number) value).doubleValue();
    }

    private EntityShape entity(Class<?> type) throws Exception {
        EntityShape retained = shapes.get(type);
        if (retained != null) return retained;
        if (nonEntities.contains(type)) return null;
        for (Class<?> root = type;
                root != null && root.getClassLoader() != null;
                root = root.getSuperclass()) {
            LegacyClassFile code = symbols.type(root);
            if (code == null) continue;
            String[] coordinates = code.positionFields();
            if (coordinates != null) {
                Field x = accessible(root.getDeclaredField(coordinates[0])),
                        y = accessible(root.getDeclaredField(coordinates[1])),
                        z = accessible(root.getDeclaredField(coordinates[2]));
                Field dimension = null;
                // Some original APIs serialize dimension on the player subtype, while XYZ belongs
                // to the base entity. Preserve that actual declaring field instead of guessing an
                // int.
                for (Class<?> owner = type;
                        owner != null && root.isAssignableFrom(owner);
                        owner = owner.getSuperclass()) {
                    LegacyClassFile serialized = symbols.type(owner);
                    String dim = serialized == null ? null : serialized.dimensionField();
                    if (dim == null) continue;
                    Field candidate = accessible(owner.getDeclaredField(dim));
                    if (dimension != null && !dimension.equals(candidate))
                        throw new IllegalStateException("GAME_DIMENSION_AMBIGUOUS");
                    dimension = candidate;
                }
                if (dimension == null
                        && symbols.dimensions
                        && !root.getName().equals(symbols.bindings.getProperty("entityClass")))
                    continue;
                retained = new EntityShape(root, x, y, z, dimension);
                shapes.put(type, retained);
                return retained;
            }
        }
        String mapped = symbols.bindings.getProperty("entityClass");
        if (mapped != null)
            for (Class<?> root = type; root != null; root = root.getSuperclass())
                if (root.getName().equals(mapped)) {
                    retained = new EntityShape(root, null, null, null, null);
                    shapes.put(type, retained);
                    return retained;
                }
        nonEntities.add(type);
        return null;
    }

    private Object sharedWorld(Object client, Object player, Class<?> root)
            throws IllegalAccessException {
        Object result = null;
        for (Field fromPlayer : fields(root)) {
            Class<?> type = fromPlayer.getType();
            if (type.isPrimitive() || type.getClassLoader() == null || root.isAssignableFrom(type))
                continue;
            Object value = fromPlayer.get(player);
            if (value == null) continue;
            for (Field fromClient : fields(client.getClass()))
                if (type.isAssignableFrom(fromClient.getType())
                        && fromClient.get(client) == value) {
                    if (result != null && result != value)
                        throw new IllegalStateException("GAME_WORLD_AMBIGUOUS");
                    result = value;
                }
        }
        return result;
    }

    private Object findServer(Object client, Class<?> world) throws IllegalAccessException {
        for (Field field : fields(client.getClass())) {
            Class<?> type = field.getType();
            if (type.isPrimitive() || type.getClassLoader() == null) continue;
            Object value = field.get(client);
            if (value == null) continue;
            for (Field member : fields(type)) {
                Class<?> component = member.getType().getComponentType();
                if (component == null) continue;
                for (Class<?> base = world;
                        base != null && base.getClassLoader() != null;
                        base = base.getSuperclass())
                    if (base.isAssignableFrom(component)) return value;
            }
        }
        return null;
    }

    private String dimension(Object player, Object world, EntityShape shape) throws Exception {
        Object value =
                shape.dimension == null
                        ? symbols.field(player, "entityDimension")
                        : shape.dimension.get(player);
        if (value == null) value = symbols.call(world, "levelDimension");
        if (value == null) {
            Object provider = symbols.field(world, "levelDimensionField");
            if (provider != null) value = symbols.call(provider, "providerType");
        }
        if (value != null && !(value instanceof Number)) {
            Object location = symbols.call(value, "keyLocation");
            if (location == null) location = symbols.call(value, "dimensionLocation");
            if (location == null) location = symbols.callStatic(value, "dimensionLocationStatic");
            if (location != null) value = location;
            String id = value.toString();
            if (id.matches("[a-z0-9_.-]+:[a-z0-9_./-]+")) return id;
            Object numeric = symbols.call(value, "dimensionId");
            if (numeric != null) value = numeric;
        }
        if (value instanceof Number) {
            int id = ((Number) value).intValue();
            return id == -1
                    ? "minecraft:the_nether"
                    : id == 0
                            ? "minecraft:overworld"
                            : id == 1 && symbols.end
                                    ? "minecraft:the_end"
                                    : "legacy:dimension_" + id;
        }
        return symbols.dimensions ? null : "minecraft:overworld";
    }

    private String findDirectory(Object start, Class<?> entity, Object client) throws Exception {
        Set<Object> visited = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        visited.add(client);
        Set<String> directories = new HashSet<String>();
        ArrayDeque<Node> queue = new ArrayDeque<Node>();
        queue.add(new Node(start, 0));
        while (!queue.isEmpty() && visited.size() < 512) {
            Node node = queue.remove();
            Object value = node.value;
            if (value == null || !visited.add(value) || entity.isInstance(value)) continue;
            if (value instanceof File) {
                File file = ((File) value).getCanonicalFile();
                String prefix =
                        gameDirectory.endsWith(File.separator)
                                ? gameDirectory
                                : gameDirectory + File.separator;
                boolean owned =
                        file.equals(new File(gameDirectory))
                                || file.getPath()
                                        .regionMatches(
                                                File.separatorChar == '\\',
                                                0,
                                                prefix,
                                                0,
                                                prefix.length());
                if (owned && new File(file, "level.dat").isFile()) directories.add(file.getPath());
                continue;
            }
            if (node.depth >= 4) continue;
            if (value instanceof Map<?, ?>) {
                if (((Map<?, ?>) value).size() < 32)
                    for (Object child : ((Map<?, ?>) value).values())
                        queue.add(new Node(child, node.depth + 1));
            } else if (value instanceof Iterable<?>) {
                int count = 0;
                for (Object child : (Iterable<?>) value) {
                    if (++count > 32) break;
                    queue.add(new Node(child, node.depth + 1));
                }
            } else if (value instanceof Object[]) {
                Object[] items = (Object[]) value;
                if (items.length < 32)
                    for (Object child : items) queue.add(new Node(child, node.depth + 1));
            } else if (value.getClass().getClassLoader() != null) {
                for (Field field : fields(value.getClass()))
                    if (!field.getType().isPrimitive()) {
                        Object child = field.get(value);
                        if (child instanceof File) queue.addFirst(new Node(child, node.depth + 1));
                        else queue.add(new Node(child, node.depth + 1));
                    }
            }
        }
        return directories.size() == 1 ? directories.iterator().next() : null;
    }

    private Boolean localChannel(Object player, Class<?> root, Object world) throws Exception {
        Set<Object> visited = Collections.newSetFromMap(new IdentityHashMap<Object, Boolean>());
        ArrayDeque<Node> queue = new ArrayDeque<Node>();
        queue.add(new Node(player, 0));
        while (!queue.isEmpty() && visited.size() < 128) {
            Node node = queue.remove();
            Object value = node.value;
            if (value == null || value == world || !visited.add(value)) continue;
            for (Class<?> type = value.getClass(); type != null; type = type.getSuperclass())
                for (Class<?> api : type.getInterfaces())
                    if (api.getName().equals("io.netty.channel.Channel")) {
                        Object address = api.getMethod("remoteAddress").invoke(value);
                        return address == null
                                ? null
                                : address.getClass()
                                        .getName()
                                        .equals("io.netty.channel.local.LocalAddress");
                    }
            if (value instanceof java.net.Socket) return false;
            if (node.depth >= 3 || value.getClass().getClassLoader() == null) continue;
            for (Field field : fields(value.getClass())) {
                if (node.depth == 0 && field.getDeclaringClass().isAssignableFrom(root)) continue;
                if (!field.getType().isPrimitive()
                        && (field.getType().getClassLoader() != null
                                || java.net.Socket.class.isAssignableFrom(field.getType())))
                    queue.add(new Node(field.get(value), node.depth + 1));
            }
        }
        return null;
    }

    private static List<Field> fields(Class<?> type) {
        List<Field> result = new ArrayList<Field>();
        for (Class<?> current = type;
                current != null && current.getClassLoader() != null;
                current = current.getSuperclass())
            for (Field field : current.getDeclaredFields())
                if (!Modifier.isStatic(field.getModifiers())) {
                    try {
                        result.add(accessible(field));
                    } catch (RuntimeException denied) {
                    }
                }
        return result;
    }

    private static Field accessible(Field field) {
        field.setAccessible(true);
        return field;
    }

    private static boolean finite(double value) {
        return !Double.isNaN(value) && !Double.isInfinite(value);
    }

    private static String timestamp() {
        SimpleDateFormat format = new SimpleDateFormat("yyyy-MM-dd'T'HH:mm:ss.SSS'Z'", Locale.ROOT);
        format.setTimeZone(TimeZone.getTimeZone("UTC"));
        return format.format(new Date());
    }

    private static int depth(Class<?> type) {
        int value = 0;
        for (; type != null; type = type.getSuperclass()) value++;
        return value;
    }

    private static final class EntityShape {
        final Class<?> root;
        final Field x, y, z, dimension;

        EntityShape(Class<?> root, Field x, Field y, Field z, Field dimension) {
            this.root = root;
            this.x = x;
            this.y = y;
            this.z = z;
            this.dimension = dimension;
        }
    }

    private static final class Node {
        final Object value;
        final int depth;

        Node(Object value, int depth) {
            this.value = value;
            this.depth = depth;
        }
    }
}
