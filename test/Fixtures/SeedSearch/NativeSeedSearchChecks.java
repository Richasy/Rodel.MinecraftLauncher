package io.github.richasy.rodel.bridge;

import java.io.File;
import java.lang.reflect.*;
import java.util.*;
import java.util.concurrent.atomic.AtomicBoolean;

/**
 * Review: independent presence conditions must not multiply expensive native queries, while related
 * conditions must share one actual parent witness. Storage tests and a single native condition cannot
 * distinguish either failure. Only generation is substituted; the production matcher and ordered
 * witness output remain real. Query counts expose repeated work without machine-dependent timings.
 */
public final class NativeSeedSearchChecks {
    public static void main(String[] args) throws Exception {
        independentRoots();
        sharedParent();
        leafBiome();
        cancellation();
        System.out.println("PASS NativeSeedSearchChecks (4 cases)");
    }

    private static void independentRoots() throws Exception {
        Fixture world = new Fixture();
        List<Object> matches = match(world, new AtomicBoolean(), Arrays.asList(
                condition("a", "origin", "many", 1),
                condition("b", "origin", "many", 1),
                condition("c", "origin", "missing", 1)));
        require(matches == null, "An absent independent condition must reject the seed.");
        require(world.queries <= 3, "Independent roots must query at most once each, not once per earlier witness: " + world.queries);
    }

    private static void sharedParent() throws Exception {
        Fixture world = new Fixture();
        List<Map<String, Object>> conditions = Arrays.asList(
                condition("parent", "origin", "many", 2),
                condition("unrelated", "origin", "many", 1),
                condition("right", "parent", "right", 1),
                condition("sibling", "parent", "left", 1));
        require(match(world, new AtomicBoolean(), conditions) == null,
                "Children at different parent anchors must never combine into a false match.");
        conditions.set(3, condition("sibling", "parent", "right", 1));
        List<Object> matches = match(world, new AtomicBoolean(), conditions);
        require(matches != null && matches.size() == 4, "A later shared parent witness must be retried.");
        for (int index = 0; index < conditions.size(); index++)
            require(conditions.get(index).get("id").equals(object(matches.get(index)).get("conditionId")),
                    "Matches must retain input order even when dependency trees interleave.");
        require(((Number) object(object(matches.get(0)).get("anchor")).get("x")).intValue() == 100,
                "Both descendants require the same later parent anchor.");
        require(((List<?>) object(matches.get(0)).get("positions")).size() >= 2,
                "Minimum counts still require distinct native positions.");
    }

    private static void leafBiome() throws Exception {
        Fixture world = new Fixture();
        Map<String, Object> biome = condition("biome", "origin", "plains", 1);
        biome.put("kind", "biome");
        biome.put("radius", 32);
        List<Object> matches = match(world, new AtomicBoolean(), Collections.singletonList(biome));
        require(matches != null && world.columns == 1,
                "A leaf presence condition needs one confirmed surface witness, not 64 height queries.");
    }

    private static void cancellation() throws Exception {
        AtomicBoolean stopped = new AtomicBoolean(true);
        try {
            match(new Fixture(), stopped, Collections.singletonList(condition("a", "origin", "many", 1)));
            throw new AssertionError("Cancellation must escape without a partial successful seed.");
        } catch (java.util.concurrent.CancellationException expected) {
        }
    }

    private static List<Object> match(Fixture world, AtomicBoolean stopped, List<Map<String, Object>> conditions) throws Exception {
        Constructor<NativeSeedSearch> constructor = NativeSeedSearch.class.getDeclaredConstructor(
                OriginalSeedWorker.class, Map.class, AtomicBoolean.class);
        constructor.setAccessible(true);
        NativeSeedSearch search = constructor.newInstance(null, Json.object("conditions", conditions, "biomeStep", 4), stopped);
        Field context = NativeSeedSearch.class.getDeclaredField("world");
        context.setAccessible(true);
        context.set(search, world);
        Method solve = NativeSeedSearch.class.getDeclaredMethod("solve", List.class);
        solve.setAccessible(true);
        List<Object> matches = new ArrayList<>(Collections.nCopies(conditions.size(), null));
        try {
            return (Boolean) solve.invoke(search, matches) ? matches : null;
        } catch (InvocationTargetException error) {
            throw (Exception) error.getCause();
        }
    }

    private static Map<String, Object> condition(String id, String reference, String target, int minimum) {
        return Json.object("id", id, "kind", "structure", "targets", Collections.singletonList(target),
                "reference", reference, "radius", 128, "minimum", minimum);
    }

    @SuppressWarnings("unchecked")
    private static Map<String, Object> object(Object value) { return (Map<String, Object>) value; }

    private static void require(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

    private static final class Fixture extends NativeSeedContext {
        int queries, columns;

        Fixture() {
            minimumY = -64;
            heightLimit = 320;
            structures = new NativeStructureSource() {
                Set<String> ids() { return new HashSet<>(Arrays.asList("many", "missing", "left", "right")); }

                List<Map<String, Object>> find(int minX, int minZ, int maxX, int maxZ, List<String> targets, AtomicBoolean stopped) {
                    queries++;
                    String target = targets.get(0);
                    int center = (minX + maxX) / 2;
                    if (target.equals("missing") || target.equals("right") && center != 100 || target.equals("left") && center != 0)
                        return new ArrayList<>();
                    List<Map<String, Object>> positions = new ArrayList<>();
                    for (int x : target.equals("many") ? new int[] {0, 100} : new int[] {center})
                        positions.add(Json.object("id", target, "kind", "structure", "x", x, "y", 64, "z", 0, "pieces", 1));
                    return positions;
                }
            };
        }

        String biome(int x, int y, int z) { return "plains"; }
        int[] column(int x, int z, Integer y, boolean terrain) { columns++; return new int[] {64}; }
        Map<String, Object> spawn() { throw new AssertionError("No spawn query expected."); }
        File save(int a, int b, int c, int d, boolean loot, AtomicBoolean stopped) { throw new AssertionError(); }
        Map<String, Object> readRegion(Map<String, Object> query, AtomicBoolean stopped) { throw new AssertionError(); }
    }
}
