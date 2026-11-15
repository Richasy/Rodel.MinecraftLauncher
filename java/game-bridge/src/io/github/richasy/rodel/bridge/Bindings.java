package io.github.richasy.rodel.bridge;

import java.io.*;
import java.lang.instrument.Instrumentation;
import java.lang.invoke.MethodHandles;
import java.lang.invoke.VarHandle;
import java.lang.reflect.*;
import java.nio.file.*;
import java.util.*;
import java.util.concurrent.ConcurrentHashMap;

/** Resolves exact generated official names, optionally through Fabric's actual mapping resolver. */
final class Bindings {
    private final Instrumentation instrumentation;
    private final Properties names = new Properties();
    private final Map<String, Method> methods = new ConcurrentHashMap<>();
    private final Map<String, VarHandle> fields = new ConcurrentHashMap<>();
    private final Set<String> reported = ConcurrentHashMap.newKeySet();
    private Class<?> clientType;
    private boolean clientInitialized;
    private Object resolver;
    private Class<?> resolverApi;
    Bindings(Instrumentation instrumentation, String file) throws IOException {
        this.instrumentation = instrumentation;
        try (InputStream input = Files.newInputStream(Path.of(file))) { names.load(input); }
    }
    boolean clockApi() { return names.getProperty("clockApi").equals("true"); }
    boolean namedApi() { return names.getProperty("namedApi", "false").equals("true"); }
    Object screen(Object client) throws Exception { return namedApi() ? call(field(client, "clientGui"), "guiScreen") : field(client, "clientScreen"); }
    void setScreen(Object client, Object screen) throws Exception { call(namedApi() ? field(client, "clientGui") : client, namedApi() ? "guiSetScreen" : "clientSetScreen", screen); }
    boolean regionApi() { return names.getProperty("regionApi", "false").equals("true"); }
    boolean has(String key) { return names.containsKey(key); }
    String raw(String key) { return names.getProperty(key); }
    Class<?> officialType(String name) throws Exception { return type(className(names.getProperty("type." + name, name))); }
    String nativeName(String key) throws Exception { return memberName(key, true); }
    void report(String operation, Throwable error) {
        StackTraceElement[] frames = error.getStackTrace();
        String location = frames.length == 0 ? "unknown" : frames[0].getClassName() + "." + frames[0].getMethodName() + ":" + frames[0].getLineNumber();
        String code = operation + ":" + error.getClass().getSimpleName() + ":" + location;
        if (reported.size() < 32 && reported.add(code)) System.err.println("[rodel-bridge] " + code);
    }
    synchronized Object client() throws Exception {
        if (clientType == null) {
            Class<?>[] loaded = instrumentation.getAllLoadedClasses();
            if (resolver == null && !clockApi()) {
                boolean gameEntryLoaded = Arrays.stream(loaded).anyMatch(type -> type.getName().equals("net.minecraft.client.main.Main"));
                for (Class<?> type : loaded) if (type.getName().equals("net.fabricmc.loader.api.FabricLoader")) {
                    // getMappingResolver may lazily initialize Fabric's mapping tree. Knot initializes
                    // that tree before loading the game entrypoint; querying it sooner races that work.
                    // Observe class definitions only: never preload Main to create this barrier.
                    if (!gameEntryLoaded) return null;
                    Object loader = type.getMethod("getInstance").invoke(null);
                    resolver = type.getMethod("getMappingResolver").invoke(loader);
                    resolverApi = Class.forName("net.fabricmc.loader.api.MappingResolver", false, type.getClassLoader());
                    break;
                }
            }
            String expected = className(names.getProperty("clientClass"));
            for (Class<?> type : loaded) if (type.getName().equals(expected)) { clientType = type; break; }
        }
        if (clientType == null) return null;
        if (!clientInitialized) {
            // A class definition is not initialization evidence. Invoking its static singleton accessor
            // sooner could move Minecraft's native initialization onto this agent thread.
            for (StackTraceElement[] frames : Thread.getAllStackTraces().values()) {
                boolean executing = false, initializing = false;
                for (StackTraceElement frame : frames) if (frame.getClassName().equals(clientType.getName())) {
                    if (frame.getMethodName().equals("<clinit>")) initializing = true;
                    else executing = true;
                }
                if (executing && !initializing) { clientInitialized = true; break; }
            }
            if (!clientInitialized) return null;
        }
        return call(null, "clientInstance");
    }
    private String className(String official) throws Exception {
        return resolver == null ? official : (String)resolverApi.getMethod("mapClassName", String.class, String.class).invoke(resolver, "official", official);
    }
    Class<?> type(String name) throws Exception { return Class.forName(name, false, clientType.getClassLoader()); }
    private Class<?> blockPosType() throws Exception { return type(className(names.getProperty("blockPosClass"))); }
    Object blockPos(int x, int y, int z) throws Exception { return blockPosType().getConstructor(int.class, int.class, int.class).newInstance(x, y, z); }
    private Class<?> owner(String key) throws Exception { return type(className(names.getProperty(key + ".owner"))); }
    private String memberName(String key, boolean method) throws Exception {
        return memberName(key, method, names.getProperty(key + ".owner"));
    }
    private String memberName(String key, boolean method, String officialOwner) throws Exception {
        String name = names.getProperty(key);
        if (resolver == null) return name;
        return (String)resolverApi.getMethod(method ? "mapMethodName" : "mapFieldName", String.class, String.class, String.class, String.class)
            .invoke(resolver, "official", officialOwner, name, names.getProperty(key + ".descriptor"));
    }
    private Method method(String key, Class<?>[] parameters) throws Exception {
        Class<?> owner = owner(key);
        try { return owner.getDeclaredMethod(memberName(key, true), parameters); }
        catch (NoSuchMethodException absent) {
            if (resolver == null) return owner.getMethod(memberName(key, true), parameters);
            // Tiny mappings can store a virtual method only on its interface/supertype. Resolve that
            // exact official signature through the real hierarchy; do not guess a field by its type.
            Set<Class<?>> visited = new HashSet<>();
            ArrayDeque<Class<?>> pending = new ArrayDeque<>(); pending.add(owner);
            while (!pending.isEmpty()) {
                Class<?> ancestor = pending.remove();
                if (!visited.add(ancestor)) continue;
                String official = (String)resolverApi.getMethod("unmapClassName", String.class, String.class).invoke(resolver, "official", ancestor.getName());
                String mapped = memberName(key, true, official);
                try { return owner.getMethod(mapped, parameters); }
                catch (NoSuchMethodException ignored) { }
                if (ancestor.getSuperclass() != null) pending.add(ancestor.getSuperclass());
                Collections.addAll(pending, ancestor.getInterfaces());
            }
            throw absent;
        }
    }
    private Method boundMethod(String key) throws Exception {
        try {
            Method method = methods.get(key);
            if (method == null) {
                // Argument types are selected by the fixed bridge implementation, never by the peer.
                String[] names = this.names.getProperty(key + ".parameters", "").split(",");
                Class<?>[] parameters = names.length == 1 && names[0].isEmpty() ? new Class<?>[0] : new Class<?>[names.length];
                for (int index = 0; index < parameters.length; index++) parameters[index] = parameterType(names[index]);
                method = method(key, parameters);
                method.setAccessible(true); methods.put(key, method);
            }
            return method;
        } catch (Exception error) {
            if (reported.add(key)) System.err.println("[rodel-bridge] Unsupported binding " + key + " (" + error.getClass().getSimpleName() + ")");
            throw error;
        }
    }
    void requireMethod(String key) throws Exception { boundMethod(key); }
    Object call(Object target, String key, Object... arguments) throws Exception {
        try { return boundMethod(key).invoke(target, arguments); }
        catch (Exception error) {
            if (reported.add(key)) System.err.println("[rodel-bridge] Unsupported binding " + key + " (" + error.getClass().getSimpleName() + ")");
            throw error;
        }
    }
    private Class<?> parameterType(String name) throws Exception {
        return switch (name) {
            case "boolean" -> boolean.class; case "byte" -> byte.class; case "short" -> short.class; case "char" -> char.class;
            case "int" -> int.class; case "long" -> long.class; case "float" -> float.class; case "double" -> double.class;
            default -> type(className(name));
        };
    }
    void setField(Object target, String key, Object value) throws Exception {
        field(target, key);
        fields.get(key).setVolatile(target, value);
    }
    Object field(Object target, String key) throws Exception {
        try {
        VarHandle field = fields.get(key);
        if (field == null) {
            Class<?> owner = owner(key);
            Field member = owner.getDeclaredField(memberName(key, false));
            field = MethodHandles.privateLookupIn(owner, MethodHandles.lookup()).unreflectVarHandle(member);
            fields.put(key, field);
        }
        // Identity checks can run on the authoritative thread: acquire the actual current reference,
        // without traversing mutable client state or using a cached local-mode assertion.
        return target == null ? field.getVolatile() : field.getVolatile(target);
        } catch (Exception error) {
            if (reported.add(key)) System.err.println("[rodel-bridge] Unsupported binding " + key + " (" + error.getClass().getSimpleName() + ")");
            throw error;
        }
    }
    static void execute(Object target, Runnable action) throws Exception { target.getClass().getMethod("execute", Runnable.class).invoke(target, action); }
    String dimension(Object level) throws Exception { return call(call(level, "levelDimension"), "keyLocation").toString(); }
    long time(Object level) throws Exception { return ((Number)call(level, "levelTime")).longValue(); }
    void time(Object level, long ticks) throws Exception {
        if (!clockApi()) { call(level, "levelSetTime", ticks); return; }
        Object dimension = type("net.minecraft.world.level.Level").getMethod("dimensionType").invoke(level);
        Optional<?> clock = (Optional<?>)type("net.minecraft.world.level.dimension.DimensionType").getMethod("defaultClock").invoke(dimension);
        Object selected = clock.orElseThrow(() -> new IllegalStateException("GAME_CLOCK_UNAVAILABLE"));
        Object manager = type("net.minecraft.server.level.ServerLevel").getMethod("clockManager").invoke(level);
        type("net.minecraft.world.clock.ServerClockManager").getMethod("setTotalTicks", type("net.minecraft.core.Holder"), long.class).invoke(manager, selected, ticks);
    }
}
