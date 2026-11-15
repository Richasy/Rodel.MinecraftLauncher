package io.github.richasy.rodel.input;

import java.util.Observer;

/** Read-only bootstrap callback; a game class loader only sees JDK types and this tiny hook. */
public final class LegacyHooks {
    private static volatile Observer observer;

    private LegacyHooks() {}

    /** Installs the observation owner before any game class is transformed. */
    public static void install(Observer value) {
        observer = value;
    }

    /**
     * Offers the calling frame to the observer, which proves thread ownership before rate limiting;
     * failures never escape into gameplay.
     */
    public static void tick(Object client) {
        Observer value = observer;
        if (value == null) return;
        try {
            value.update(null, client);
        } catch (Throwable ignored) {
        }
    }
}
