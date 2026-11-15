package dev.richasy.worldreroll;

import java.util.concurrent.atomic.AtomicReference;

/** One-shot randomness control, included only in the isolated client test mod. */
public final class RerollSeedFixture {
    private static final AtomicReference<Long> NEXT = new AtomicReference<>();

    private RerollSeedFixture() {}

    /** Supplies the next replacement seed without replacing native world generation or spawn placement. */
    public static void next(long seed) { NEXT.set(seed); }

    /** Consumes the test seed once; unrelated native calls retain their normal randomness. */
    public static Long take() { return NEXT.getAndSet(null); }

    /** Prevents an interrupted fixture from influencing the next scenario. */
    public static void clear() { NEXT.set(null); }
}
