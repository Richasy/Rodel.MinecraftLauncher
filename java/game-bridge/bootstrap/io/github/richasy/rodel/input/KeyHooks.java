package io.github.richasy.rodel.input;

import java.util.*;
import java.util.function.BooleanSupplier;

/** Bootstrap-only hook: all payloads use JDK types so game class loaders need no dependency on the agent. */
public final class KeyHooks {
    private static volatile Map<Object, BooleanSupplier> inputs = Map.of();
    private static volatile boolean transformed;
    private KeyHooks() { }
    /** Preserves physical input and adds an override only while its current-world predicate allows it. */
    public static boolean resolve(Object key, boolean physical) {
        BooleanSupplier input = inputs.get(key);
        if (input == null) return physical;
        try { return physical || input.getAsBoolean(); }
        catch (Throwable unavailable) { return physical; }
    }
    /** Replaces one bounded input lease atomically; identity keys never collide by translated names. */
    public static void install(Map<Object, BooleanSupplier> values) { inputs = Collections.unmodifiableMap(new IdentityHashMap<>(values)); }
    /** Removes all agent input without changing any physical key state. */
    public static void clear() { inputs = Map.of(); }
    /** Records successful transformation of the exact native boolean getter. */
    public static void transformed() { transformed = true; }
    /** Indicates that the native key getter, rather than a synthetic polling fallback, owns input resolution. */
    public static boolean available() { return transformed; }
}
