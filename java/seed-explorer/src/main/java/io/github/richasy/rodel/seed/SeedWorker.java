package io.github.richasy.rodel.seed;

import com.google.gson.Gson;

import java.io.*;
import java.nio.charset.StandardCharsets;
import java.nio.file.Path;
import java.util.concurrent.*;
import java.util.concurrent.atomic.AtomicBoolean;

/** Owns one original-game runtime, bounded requests and cooperative cancellation. */
public final class SeedWorker {
    private static final Gson JSON = new Gson();
    private final PrintWriter output;
    private final ConcurrentHashMap<String, AtomicBoolean> cancellations =
            new ConcurrentHashMap<>();
    private final ExecutorService executor = Executors.newSingleThreadExecutor();
    private final VanillaEngine engine;

    private SeedWorker(PrintStream protocolOutput) throws Exception {
        output =
                new PrintWriter(
                        new OutputStreamWriter(protocolOutput, StandardCharsets.UTF_8), true);
        engine = new VanillaEngine(Path.of("workspace"));
    }

    private synchronized void send(String id, String kind, Object data, String error) {
        output.println(JSON.toJson(new Protocol.Envelope(id, kind, data, error)));
    }

    private void run() throws Exception {
        send(
                null,
                "ready",
                new Protocol.Ready(1, net.minecraft.SharedConstants.getCurrentVersion().id()),
                null);
        try (var input =
                new BufferedReader(new InputStreamReader(System.in, StandardCharsets.UTF_8))) {
            String line;
            while ((line = input.readLine()) != null) {
                if (line.length() > 1_048_576)
                    throw new IOException("Seed request exceeds one MiB.");
                var request = JSON.fromJson(line, Protocol.Request.class);
                if ("cancel".equals(request.operation())) {
                    var cancelled = cancellations.get(request.cancelId());
                    if (cancelled != null) cancelled.set(true);
                    continue;
                }
                if (request.id() == null || request.id().length() > 80)
                    throw new IOException("Missing request identity.");
                if (cancellations.size() >= 4) {
                    send(request.id(), "error", null, "Worker request queue is full.");
                    continue;
                }
                var cancelled = new AtomicBoolean();
                if (cancellations.putIfAbsent(request.id(), cancelled) != null)
                    throw new IOException("Duplicate request identity.");
                executor.submit(
                        () -> {
                            try {
                                Object result =
                                        switch (request.operation()) {
                                            case "catalog" -> engine.catalog();
                                            case "seed" ->
                                                    new Protocol.SeedValue(
                                                            Long.toString(
                                                                    net.minecraft.world.level
                                                                            .levelgen.WorldOptions
                                                                            .parseSeed(
                                                                                    request.seed()
                                                                                                    == null
                                                                                            ? ""
                                                                                            : request
                                                                                                    .seed())
                                                                            .orElseGet(
                                                                                    net.minecraft
                                                                                                    .world
                                                                                                    .level
                                                                                                    .levelgen
                                                                                                    .WorldOptions
                                                                                            ::randomSeed)));
                                            case "map" -> engine.map(request.map(), cancelled);
                                            case "surface" -> VanillaSurface.map(Path.of("surfaces"), request.map(), cancelled);
                                            case "spawn" ->
                                                    engine.spawn(request.spawn(), cancelled);
                                            case "tints" -> engine.tint(request.tints(), cancelled);
                                            case "loot" ->
                                                    VanillaRegion.predictLoot(
                                                            Path.of("loot-predictions"),
                                                            request.loot(),
                                                            cancelled);
                                            case "structures" ->
                                                    engine.findStructures(
                                                            request.structures(), cancelled);
                                            case "region" ->
                                                    VanillaRegion.generate(
                                                            Path.of("regions"),
                                                            request.region(),
                                                            cancelled,
                                                            snapshot ->
                                                                    send(
                                                                            request.id(),
                                                                            "progress",
                                                                            snapshot,
                                                                            null));
                                            case "search" ->
                                                    engine.search(
                                                            request.search(),
                                                            cancelled,
                                                            snapshot ->
                                                                    send(
                                                                            request.id(),
                                                                            "progress",
                                                                            snapshot,
                                                                            null));
                                            default ->
                                                    throw new IllegalArgumentException(
                                                            "Unknown seed operation.");
                                        };
                                send(request.id(), "result", result, null);
                            } catch (Throwable exception) {
                                if (isCancellation(exception)) {
                                    send(request.id(), "cancelled", null, null);
                                } else {
                                    exception.printStackTrace(System.err);
                                    send(
                                            request.id(),
                                            "error",
                                            null,
                                            exception.getClass().getSimpleName()
                                                    + ": "
                                                    + exception.getMessage());
                                }
                            } finally {
                                cancellations.remove(request.id());
                            }
                        });
            }
        } finally {
            cancellations.values().forEach(flag -> flag.set(true));
            executor.shutdown();
            if (!executor.awaitTermination(10, TimeUnit.SECONDS)) executor.shutdownNow();
            engine.close();
        }
    }

    /** Recognizes cancellation across async wrappers without hiding genuine engine failures. */
    static boolean isCancellation(Throwable exception) {
        while ((exception instanceof CompletionException || exception instanceof ExecutionException)
                && exception.getCause() != null) exception = exception.getCause();
        return exception instanceof CancellationException;
    }

    /** Runs only as the launcher's internal child, with no game or player directory access. */
    public static void main(String[] args) throws Exception {
        var protocolOutput = System.out;
        System.setOut(new PrintStream(System.err, true, StandardCharsets.UTF_8));
        if (args.length != 1)
            throw new IllegalArgumentException("A parent process identity is required.");
        var parent = ProcessHandle.of(Long.parseLong(args[0]));
        var watcher =
                new Thread(
                        () -> {
                            try {
                                parent.ifPresent(handle -> handle.onExit().join());
                            } finally {
                                System.exit(0);
                            }
                        },
                        "seed-parent-lifetime");
        watcher.setDaemon(true);
        watcher.start();
        try {
            new SeedWorker(protocolOutput).run();
        } catch (Throwable error) {
            error.printStackTrace(System.err);
            System.exit(1);
        }
        System.exit(0);
    }
}
