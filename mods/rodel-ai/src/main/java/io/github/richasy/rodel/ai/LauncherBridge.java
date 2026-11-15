package io.github.richasy.rodel.ai;

import java.lang.reflect.InvocationTargetException;
import java.util.concurrent.CompletableFuture;
import com.google.gson.JsonObject;
import com.google.gson.JsonParser;

/** The only game-side launcher dependency is the exact optional startup-agent API. */
final class LauncherBridge {
    private LauncherBridge() {}

    @SuppressWarnings("unchecked")
    static CompletableFuture<JsonObject> request(JsonObject request) {
        try {
            Class<?> agent = Class.forName("io.github.richasy.rodel.bridge.Agent", false, ClassLoader.getSystemClassLoader());
            var future = (CompletableFuture<String>) agent.getMethod("requestAi", String.class).invoke(null, request.toString());
            return future.thenApply(value -> JsonParser.parseString(value).getAsJsonObject());
        } catch (ClassNotFoundException | NoSuchMethodException error) {
            return CompletableFuture.failedFuture(new IllegalStateException("GAME_AI_BRIDGE_REQUIRED"));
        } catch (InvocationTargetException error) {
            return CompletableFuture.failedFuture(error.getCause());
        } catch (ReflectiveOperationException error) {
            return CompletableFuture.failedFuture(new IllegalStateException("GAME_AI_BRIDGE_UNAVAILABLE", error));
        }
    }
}
