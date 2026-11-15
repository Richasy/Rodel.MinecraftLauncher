package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;
import java.util.function.Consumer;

/**
 * Bounded positive-witness matching; every biome, spawn and assembled structure is supplied by
 * original game APIs.
 */
final class NativeSeedSearch {
    private final OriginalSeedWorker worker;
    private final Map<String, Object> query;
    private final List<Map<String, Object>> conditions;
    private final AtomicBoolean stopped;
    private NativeSeedContext world;

    @SuppressWarnings("unchecked")
    private NativeSeedSearch(
            OriginalSeedWorker worker, Map<String, Object> query, AtomicBoolean stopped) {
        this.worker = worker;
        this.query = query;
        this.stopped = stopped;
        conditions = (List<Map<String, Object>>) query.get("conditions");
    }

    static Map<String, Object> run(
            OriginalSeedWorker worker,
            Map<String, Object> query,
            AtomicBoolean cancelled,
            Consumer<Map<String, Object>> progress)
            throws Exception {
        int maxSeeds = number(query, "maxSeeds"),
                maxResults = number(query, "maxResults"),
                seconds = number(query, "timeLimitSeconds");
        long offset = ((Number) query.get("offset")).longValue(),
                first = Long.parseLong(Json.string(query, "startSeed"));
        if (maxSeeds < 1
                || maxSeeds > 1000000
                || maxResults < 1
                || maxResults > 100
                || seconds < 1
                || seconds > 3600
                || offset < 0) throw new IllegalArgumentException("SEED_SEARCH_BUDGET");
        AtomicBoolean stopped = new AtomicBoolean();
        NativeSeedSearch search = new NativeSeedSearch(worker, query, stopped);
        if (search.conditions == null
                || search.conditions.isEmpty()
                || search.conditions.size() > 12
                || number(query, "biomeStep") < 4
                || number(query, "biomeStep") > 256)
            throw new IllegalArgumentException("SEED_SEARCH_CONDITIONS");
        long start = System.nanoTime(),
                deadline = start + seconds * 1000000000L,
                lastProgress = start,
                checked = 0;
        List<Object> hits = new ArrayList<>();
        String status = "budget_exhausted";
        ScheduledExecutorService clock =
                Executors.newSingleThreadScheduledExecutor(
                        action -> {
                            Thread thread = new Thread(action, "seed-search-deadline");
                            thread.setDaemon(true);
                            return thread;
                        });
        clock.scheduleAtFixedRate(
                () -> {
                    if (cancelled.get() || System.nanoTime() >= deadline) stopped.set(true);
                },
                0,
                20,
                TimeUnit.MILLISECONDS);
        try {
            for (; checked < maxSeeds; checked++) {
                if (cancelled.get()) {
                    status = "cancelled";
                    break;
                }
                if (System.nanoTime() >= deadline) {
                    status = "time_limit";
                    break;
                }
                String seed = Long.toString(first + offset + checked);
                search.world = worker.world(Json.object("world", query.get("world"), "seed", seed));
                search.validate();
                List<Object> matches = new ArrayList<>(Collections.nCopies(search.conditions.size(), null));
                try {
                    if (search.solve(matches))
                        hits.add(Json.object("seed", seed, "matches", new ArrayList<>(matches)));
                } catch (CancellationException stop) {
                    status = cancelled.get() ? "cancelled" : "time_limit";
                    break;
                }
                if (hits.size() >= maxResults) {
                    checked++;
                    status = "completed";
                    break;
                }
                if (System.nanoTime() - lastProgress >= 250000000L) {
                    progress.accept(snapshot("running", checked + 1, offset, hits, start));
                    lastProgress = System.nanoTime();
                }
            }
            return snapshot(status, checked, offset, hits, start);
        } finally {
            clock.shutdownNow();
        }
    }

    private static Map<String, Object> snapshot(
            String status, long checked, long offset, List<Object> hits, long start) {
        return Json.object(
                "status",
                status,
                "checked",
                checked,
                "nextOffset",
                offset + checked,
                "hits",
                new ArrayList<>(hits),
                "elapsedMilliseconds",
                (System.nanoTime() - start) / 1000000.0);
    }

    private void validate() {
        Set<String> identifiers = new HashSet<>(Arrays.asList("origin", "spawn"));
        for (Map<String, Object> condition : conditions) {
            String id = Json.string(condition, "id"), kind = Json.string(condition, "kind");
            List<String> targets = targets(condition);
            Object reference = condition.get("reference");
            if ("spawn".equals(reference) && !world.seedBoundSpawn())
                throw new IllegalArgumentException(
                        "SEED_SPAWN_NOT_SEED_BOUND: Native spawn is not determined by this seed"
                            + " alone.");
            if (id.trim().isEmpty()
                    || !identifiers.add(id)
                    || targets == null
                    || targets.isEmpty()
                    || targets.size() > 128
                    || number(condition, "minimum") < 1
                    || number(condition, "minimum") > 64
                    || number(condition, "radius") < 0
                    || number(condition, "radius") > 8192
                    || reference != null && !identifiers.contains(reference)
                    || id.equals(reference))
                throw new IllegalArgumentException("Invalid native search dependency or bounds.");
            if (kind.equals("biome")) {
                if (number(condition, "minimum") != 1
                        || !new HashSet<>(world.biomeIds.values()).containsAll(targets))
                    throw new IllegalArgumentException("Invalid native biome condition.");
            } else if (!kind.equals("structure") || !world.structures.ids().containsAll(targets))
                throw new IllegalArgumentException("Invalid native structure condition.");
        }
    }

    private boolean solve(List<Object> matches) throws Exception {
        // A condition has a single parent. Backtrack within that tree, never across unrelated roots.
        for (int index = 0; index < conditions.size(); index++) {
            check();
            Object source = conditions.get(index).get("reference");
            if (source != null && !source.equals("origin") && !source.equals("spawn")) continue;
            Map<String, Object> reference = source != null && source.equals("spawn")
                    ? world.spawn()
                    : Json.object("id", "origin", "kind", "origin", "x", 0, "y", 0, "z", 0, "pieces", 0);
            if (!solveCondition(index, reference, matches)) return false;
        }
        return true;
    }

    private boolean solveCondition(int index, Map<String, Object> reference, List<Object> matches)
            throws Exception {
        check();
        Map<String, Object> condition = conditions.get(index);
        List<Integer> children = new ArrayList<>();
        for (int child = index + 1; child < conditions.size(); child++)
            if (Json.string(condition, "id").equals(conditions.get(child).get("reference"))) children.add(child);
        List<Map<String, Object>> positions;
        if (Json.string(condition, "kind").equals("structure")) {
            int x = number(reference, "x"),
                    z = number(reference, "z"),
                    radius = number(condition, "radius");
            positions =
                    world.structures.find(
                            x - radius,
                            z - radius,
                            x + radius,
                            z + radius,
                            targets(condition),
                            stopped);
            positions.removeIf(value -> squared(value, reference) > (long) radius * radius);
        } else positions = biomes(reference, condition, children.isEmpty() ? 1 : 64);
        if (positions.size() < number(condition, "minimum")) return false;
        String id = Json.string(condition, "id");
        for (Map<String, Object> anchor : positions) {
            check();
            List<Object> witnesses = new ArrayList<>();
            witnesses.add(anchor);
            for (Map<String, Object> witness : positions) {
                if (witness != anchor) witnesses.add(witness);
                if (witnesses.size() == 64) break;
            }
            boolean accepted = true;
            for (int child : children)
                if (!solveCondition(child, anchor, matches)) {
                    accepted = false;
                    break;
                }
            if (accepted) {
                matches.set(index, Json.object("conditionId", id, "anchor", anchor, "positions", witnesses));
                return true;
            }
        }
        return false;
    }

    private List<Map<String, Object>> biomes(
            Map<String, Object> reference, Map<String, Object> condition, int limit) throws Exception {
        List<Map<String, Object>> positions = new ArrayList<>();
        int radius = number(condition, "radius"), step = number(query, "biomeStep");
        Integer slice = condition.get("y") == null ? null : number(condition, "y");
        if (slice != null && (slice < world.minimumY || slice >= world.heightLimit))
            throw new IllegalArgumentException("Search Y is outside the original dimension.");
        for (int z = -radius; z <= radius; z += step)
            for (int x = -radius; x <= radius; x += step) {
                check();
                if ((long) x * x + (long) z * z > (long) radius * radius) continue;
                int wx = number(reference, "x") + x, wz = number(reference, "z") + z;
                String biome = world.biome(wx, slice == null ? 64 : slice, wz);
                if (!targets(condition).contains(biome)) continue;
                int y = slice == null ? world.column(wx, wz, null, false)[0] : slice;
                positions.add(
                        Json.object(
                                "id", biome, "kind", "biome", "biome", biome, "x", wx, "y", y, "z",
                                wz, "pieces", 0));
                if (positions.size() == limit) return positions;
            }
        return positions;
    }

    private void check() {
        if (stopped.get()) throw new CancellationException();
    }

    private static int number(Map<String, Object> value, String key) {
        return ((Number) value.get(key)).intValue();
    }

    @SuppressWarnings("unchecked")
    private static List<String> targets(Map<String, Object> condition) {
        return (List<String>) condition.get("targets");
    }

    private static long squared(Map<String, Object> value, Map<String, Object> reference) {
        long x = (long) number(value, "x") - number(reference, "x"),
                z = (long) number(value, "z") - number(reference, "z");
        return x * x + z * z;
    }
}
