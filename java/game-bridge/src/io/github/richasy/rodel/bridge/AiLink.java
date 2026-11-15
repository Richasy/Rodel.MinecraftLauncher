package io.github.richasy.rodel.bridge;

import java.util.*;
import java.util.concurrent.*;

/** Multiplexes game-originated AI requests on the already authenticated launcher connection. */
final class AiLink {
    private final Wire wire;
    private final GameAccess game;
    private final Executor executor;
    private final ConcurrentMap<String, CompletableFuture<String>> pending = new ConcurrentHashMap<>();
    private volatile boolean closed;

    AiLink(Wire wire, GameAccess game, Executor executor) {
        this.wire = wire;
        this.game = game;
        this.executor = executor;
    }

    CompletableFuture<String> request(String json) {
        if (closed) return CompletableFuture.failedFuture(new IllegalStateException("GAME_BRIDGE_DISCONNECTED"));
        if (json == null || json.length() > 131072) return CompletableFuture.failedFuture(new IllegalArgumentException("INVALID_PARAMS"));
        if (pending.size() >= 8) return CompletableFuture.failedFuture(new IllegalStateException("GAME_BRIDGE_BUSY"));
        String id = "ai-" + UUID.randomUUID().toString().replace("-", "");
        CompletableFuture<String> result = new CompletableFuture<>();
        pending.put(id, result);
        result.orTimeout(30, TimeUnit.SECONDS).whenComplete((value, error) -> pending.remove(id));
        try {
            executor.execute(() -> {
                try {
                    if (closed || result.isDone()) throw new IllegalStateException("GAME_BRIDGE_DISCONNECTED");
                    Map<String, Object> parameters = Json.parse(json);
                    // The UI never supplies the authorization snapshot. Sample on the real game executor.
                    if (!(parameters.remove("clientWorldId") instanceof Number world)) throw new IllegalArgumentException("GAME_WORLD_STALE");
                    parameters.put("snapshot", game.snapshotForAi(world.intValue()));
                    if (closed || result.isDone()) throw new IllegalStateException("GAME_BRIDGE_DISCONNECTED");
                    wire.write(Json.object("jsonrpc", "2.0", "id", id, "method", "launcher.ai", "params", parameters));
                } catch (Exception error) { result.completeExceptionally(error instanceof ExecutionException && error.getCause() != null ? error.getCause() : error); }
            });
        } catch (RejectedExecutionException full) { result.completeExceptionally(new IllegalStateException("GAME_BRIDGE_BUSY")); }
        return result;
    }

    void response(Map<String, Object> response) {
        String id = Json.string(response, "id");
        if (!"2.0".equals(response.get("jsonrpc")) || response.containsKey("method")
            || response.containsKey("result") == response.containsKey("error")) throw new IllegalArgumentException("INVALID_RESPONSE");
        CompletableFuture<String> result = pending.remove(id);
        if (result == null) return;
        if (response.get("error") instanceof Map<?, ?> error)
            result.completeExceptionally(new IllegalStateException(String.valueOf(error.get("message"))));
        else result.complete(Json.encode(response.get("result")));
    }

    void close() {
        closed = true;
        pending.forEach((id, result) -> result.completeExceptionally(new IllegalStateException("GAME_BRIDGE_DISCONNECTED")));
        pending.clear();
    }
}
