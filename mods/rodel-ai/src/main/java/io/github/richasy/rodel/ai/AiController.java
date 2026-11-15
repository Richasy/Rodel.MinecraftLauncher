package io.github.richasy.rodel.ai;

import java.util.*;
import java.util.concurrent.CompletionException;
import com.google.gson.JsonObject;
import com.google.gson.JsonArray;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.ClickEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.world.level.storage.LevelResource;

/** Coordinates asynchronous launcher requests; all visible state is committed on the game thread. */
final class AiController {
    private Minecraft client;
    private String worldKey;
    private String worldName;
    private int generation;
    private boolean busy;
    private boolean mutating;
    private PendingRequest deferred;
    private long nextRead;
    private JsonObject state = new JsonObject();
    private final List<JsonObject> turns = new ArrayList<>();
    private final Map<String, StringBuilder> partial = new HashMap<>();
    private final Map<String, Quick> questions = new LinkedHashMap<>();
    private String eventTurn;
    private long cursor;
    private Component error;
    private JsonObject retry;
    private int revision;
    private boolean hasOlder;
    private String draft = "";
    private long draftRevision;
    private Runnable retryAccepted;
    private boolean uncertainSubmission;

    List<JsonObject> turns() { return List.copyOf(turns); }
    boolean busy() { return mutating || deferred != null; }
    Component error() { return error; }
    int revision() { return revision; }
    boolean hasOlder() { return hasOlder; }
    String worldName() { return worldName == null ? "" : worldName; }
    String model() { return text(state, "modelName"); }
    boolean loaded() { return state.has("modelAvailable"); }
    boolean modelAvailable() { return loaded() && state.get("modelAvailable").getAsBoolean(); }
    String draft() { return draft; }
    long draftRevision() { return draftRevision; }
    void draft(String value) { if (!draft.equals(value)) { draft = value; draftRevision++; } }
    boolean retryPending() { return retry != null && uncertainSubmission; }
    String answer(JsonObject turn) {
        String answer = text(turn, "answer");
        if (!answer.isEmpty()) return answer;
        StringBuilder pending = partial.get(text(turn, "id"));
        return pending == null ? "" : pending.toString();
    }

    void tick() {
        updateWorld();
        if (worldKey == null) return;
        long now = System.currentTimeMillis();
        if (client.gui.screen() instanceof AiChatScreen && !busy && now >= nextRead) {
            nextRead = now + 500;
            request(base("read"), false, null);
        }
        for (Quick quick : List.copyOf(questions.values())) {
            if (!quick.busy && now >= quick.nextRead) {
                quick.nextRead = now + 750;
                JsonObject request = base("read");
                request.addProperty("quickId", quick.id);
                quickRequest(quick, request);
                break;
            }
        }
    }

    private void updateWorld() {
        // FML constructs client mods before Minecraft publishes its singleton. Bind only when observing a world.
        client = Minecraft.getInstance();
        if (client == null) { worldKey = null; worldName = null; return; }
        String key = null, name = null;
        if (client.level != null && client.player != null) {
            var server = client.getSingleplayerServer();
            var remote = client.getCurrentServer();
            if (server != null) {
                key = "local:" + server.getWorldPath(LevelResource.ROOT).toAbsolutePath().normalize();
                name = server.getWorldData().getLevelName();
            } else if (remote != null) {
                key = "server:" + remote.ip;
                name = remote.name;
            }
        }
        if (Objects.equals(worldKey, key)) return;
        worldKey = key; worldName = name; generation++;
        busy = false; mutating = false; deferred = null; state = new JsonObject(); turns.clear(); partial.clear(); questions.clear();
        eventTurn = null; cursor = 0; error = null; retry = null; retryAccepted = null; uncertainSubmission = false; draft(""); hasOlder = false; revision++;
    }

    void open(Screen parent) {
        updateWorld();
        if (worldKey == null) { local(Component.translatable("rai.error.world")); return; }
        client.gui.setScreen(new AiChatScreen(parent, this));
        nextRead = 0;
    }

    private JsonObject base(String action) {
        JsonObject request = new JsonObject();
        request.addProperty("action", action);
        request.addProperty("worldKey", worldKey);
        request.addProperty("worldName", worldName);
        request.addProperty("clientWorldId", System.identityHashCode(client.level));
        if (action.equals("read")) {
            if (eventTurn != null) request.addProperty("turnId", eventTurn);
            request.addProperty("after", cursor);
        }
        return request;
    }

    void send(String message, Runnable accepted) {
        if (busy() || message.isBlank()) return;
        JsonObject waiting = waiting();
        JsonObject request;
        if (waiting != null && waiting.has("question")) {
            request = base("answer");
            request.addProperty("turnId", text(waiting, "id"));
            request.addProperty("invocationId", text(waiting.getAsJsonObject("question"), "id"));
        } else {
            request = base("send");
            request.addProperty("requestId", id());
            eventTurn = null; cursor = 0;
        }
        request.addProperty("message", message);
        request(request, true, accepted);
    }

    void reset() { reset(null); }

    void resetConsole() {
        updateWorld();
        if (worldKey == null) { local(Component.translatable("rai.error.world")); return; }
        reset(() -> local(Component.translatable("rai.quick.new")));
    }

    private void reset(Runnable accepted) {
        if (busy()) return;
        JsonObject request = base("new");
        request.addProperty("requestId", id());
        request(request, true, () -> {
            questions.clear(); turns.clear(); partial.clear(); eventTurn = null; cursor = 0; hasOlder = false;
            draft(""); revision++;
            if (accepted != null) accepted.run();
        });
    }

    void cancel() {
        JsonObject waiting = waiting();
        if (waiting == null || busy()) return;
        JsonObject request = base("cancel");
        request.addProperty("turnId", text(waiting, "id"));
        request(request, true, null);
    }

    void decide(boolean approve) {
        JsonObject waiting = waiting();
        if (waiting == null || !waiting.has("confirmation") || busy()) return;
        JsonObject confirmation = waiting.getAsJsonObject("confirmation");
        JsonObject request = base("confirm");
        request.addProperty("turnId", text(waiting, "id"));
        request.addProperty("invocationId", text(confirmation, "invocationId"));
        request.addProperty("fingerprint", text(confirmation, "fingerprint"));
        request.addProperty("approve", approve);
        request(request, true, null);
    }

    void choose(String optionId, boolean skip) {
        JsonObject waiting = waiting();
        if (waiting == null || !waiting.has("question") || busy()) return;
        JsonObject request = base("answer");
        request.addProperty("turnId", text(waiting, "id"));
        request.addProperty("invocationId", text(waiting.getAsJsonObject("question"), "id"));
        JsonArray options = new JsonArray();
        if (optionId != null) options.add(optionId);
        request.add("optionIds", options);
        request.addProperty("skip", skip);
        request(request, true, null);
    }

    void older() {
        if (busy() || !hasOlder) return;
        JsonObject request = base("read");
        request.addProperty("offset", turns.size());
        request(request, false, null);
    }

    void retry() {
        if (busy()) return;
        if (retry == null) request(base("read"), false, null);
        else {
            JsonObject request = retry.deepCopy();
            request.addProperty("clientWorldId", System.identityHashCode(client.level));
            request(request, true, retryAccepted);
        }
    }

    JsonObject waiting() {
        for (JsonObject turn : turns) if (!terminal(text(turn, "status"))) return turn;
        return null;
    }

    private void request(JsonObject request, boolean mutation, Runnable accepted) {
        // A background read must not disable or swallow a player's action. Serialize one explicit
        // action behind that read, retaining its original identity and captured input.
        if (busy) { if (deferred == null) deferred = new PendingRequest(request.deepCopy(), mutation, accepted); return; }
        int ownGeneration = generation;
        busy = true;
        mutating = mutation;
        LauncherBridge.request(request).whenComplete((result, failure) -> client.execute(() -> {
            if (ownGeneration != generation) return;
            busy = false;
            mutating = false;
            try {
                if (failure != null) {
                    error = explain(failure);
                    if (mutation) {
                        retry = request.deepCopy(); retryAccepted = accepted;
                        String code = failureCode(failure);
                        uncertainSubmission = code.contains("DISCONNECTED") || code.contains("Timeout") || code.equals("GAME_BRIDGE_TIMEOUT") || code.equals("GAME_AI_REQUEST_FAILED");
                    }
                    revision++; return;
                }
                boolean recovered = retry != null && retry.has("requestId") && result.getAsJsonArray("turns").asList().stream()
                    .anyMatch(value -> text(value.getAsJsonObject(), "submissionId").equals(text(retry, "requestId")));
                Runnable recoveredAccepted = recovered ? retryAccepted : null;
                if (mutation || retry == null || recovered) { error = null; retry = null; retryAccepted = null; uncertainSubmission = false; }
                if (recoveredAccepted != null && recoveredAccepted != accepted) recoveredAccepted.run();
                if (accepted != null) accepted.run();
                apply(result, request.has("offset"));
            } finally {
                if (deferred != null) {
                    PendingRequest next = deferred; deferred = null;
                    request(next.request(), next.mutation(), next.accepted());
                }
            }
        }));
    }

    private record PendingRequest(JsonObject request, boolean mutation, Runnable accepted) {}

    /** Commits a launcher response on the client thread, preserving older history and the exact event cursor. */
    void apply(JsonObject result, boolean olderPage) {
        if (!text(state, "conversationId").equals(text(result, "conversationId"))) {
            turns.clear(); partial.clear(); eventTurn = null; cursor = 0; hasOlder = false;
        }
        state = result;
        Map<String, JsonObject> merged = new LinkedHashMap<>();
        if (!olderPage) for (JsonObject turn : turns) merged.put(text(turn, "id"), turn);
        for (var value : result.getAsJsonArray("turns")) {
            JsonObject turn = value.getAsJsonObject();
            merged.put(text(turn, "id"), turn);
        }
        if (olderPage) for (JsonObject turn : turns) merged.put(text(turn, "id"), turn);
        turns.clear(); turns.addAll(merged.values());
        if (olderPage || turns.size() <= 20) hasOlder = result.has("hasOlder") && result.get("hasOlder").getAsBoolean();
        String incomingTurn = text(result, "eventTurnId");
        if (!incomingTurn.equals(eventTurn)) { eventTurn = incomingTurn; cursor = 0; }
        if (result.has("events")) {
            JsonObject events = result.getAsJsonObject("events");
            for (var value : events.getAsJsonArray("items")) {
                JsonObject event = value.getAsJsonObject();
                if (event.get("sequence").getAsLong() <= cursor) continue;
                String kind = text(event, "kind");
                var answer = partial.computeIfAbsent(incomingTurn, ignored -> new StringBuilder());
                if (kind.equals("message_started")) answer.setLength(0);
                if (kind.equals("text_delta") && event.has("data")) answer.append(text(event.getAsJsonObject("data"), "text"));
            }
            cursor = events.get("nextSequence").getAsLong();
            if (events.get("hasMore").getAsBoolean()) nextRead = 0;
        }
        if (!turns.isEmpty()) {
            String latest = text(turns.getLast(), "id");
            if (!latest.equals(eventTurn)) { eventTurn = latest; cursor = 0; }
        }
        revision++;
    }

    void quick(String message) {
        updateWorld();
        if (worldKey == null) { local(Component.translatable("rai.error.world")); return; }
        Quick quick = new Quick(id());
        questions.put(quick.id, quick);
        local(Component.translatable("rai.quick.question", message));
        quick.status = Component.translatable("rai.quick.responding");
        local(quick.status);
        JsonObject request = base("quick");
        request.addProperty("requestId", quick.id);
        request.addProperty("message", message);
        quickRequest(quick, request);
    }

    private void quickRequest(Quick quick, JsonObject request) {
        int ownGeneration = generation;
        quick.busy = true;
        LauncherBridge.request(request).whenComplete((result, failure) -> client.execute(() -> {
            if (ownGeneration != generation || questions.get(quick.id) != quick) return;
            quick.busy = false;
            if (failure != null) {
                replace(quick, explain(failure));
                if (failure.toString().contains("DISCONNECTED") || failure.toString().contains("Timeout") || failure.toString().contains("GAME_AI_REQUEST_FAILED")) quick.nextRead = System.currentTimeMillis() + 3000;
                else questions.remove(quick.id);
                return;
            }
            JsonArray turns = result.getAsJsonArray("turns");
            if (turns.isEmpty()) {
                replace(quick, Component.translatable("rai.quick.uncertain"));
                questions.remove(quick.id); return;
            }
            JsonObject turn = turns.get(turns.size() - 1).getAsJsonObject();
            quick.turn = turn;
            if (terminal(text(turn, "status"))) {
                replace(quick, Component.translatable("rai.status." + text(turn, "status")));
                String answer = text(turn, "answer");
                if (!answer.isBlank()) local(Component.literal("[Rodel AI] " + answer));
                else if (turn.has("error")) local(Component.translatable("rai.error.request", errorText(turn.getAsJsonObject("error"))));
                questions.remove(quick.id);
            } else if (turn.has("confirmation")) {
                String invocation = text(turn.getAsJsonObject("confirmation"), "invocationId");
                if (!invocation.equals(quick.waiting)) {
                    quick.waiting = invocation;
                    replace(quick, Component.translatable("rai.quick.confirm", text(turn.getAsJsonObject("confirmation"), "name"))
                        .append(action("rai.approve", quick.id + " approve"))
                        .append(action("rai.reject", quick.id + " reject")));
                }
            } else if (quick.waiting != null || !text(turn, "status").equals(quick.lastStatus)) {
                quick.waiting = null;
                quick.lastStatus = text(turn, "status");
                replace(quick, Component.literal("[Rodel AI] ").append(Component.translatable("rai.status." + quick.lastStatus)));
            }
        }));
    }

    void quickDecision(String input) {
        String[] parts = input.split(" ", 3);
        Quick quick = parts.length < 2 ? null : questions.get(parts[0]);
        if (quick == null || quick.turn == null || quick.busy) return;
        JsonObject request = base("confirm");
        request.addProperty("quickId", quick.id);
        request.addProperty("turnId", text(quick.turn, "id"));
        if (parts[1].equals("approve") || parts[1].equals("reject")) {
            if (!quick.turn.has("confirmation")) return;
            var confirmation = quick.turn.getAsJsonObject("confirmation");
            request.addProperty("action", "confirm");
            request.addProperty("invocationId", text(confirmation, "invocationId"));
            request.addProperty("fingerprint", text(confirmation, "fingerprint"));
            request.addProperty("approve", parts[1].equals("approve"));
        } else return;
        quickRequest(quick, request);
    }

    private static Component action(String key, String command) {
        return Component.translatable(key).withStyle(style -> style.withUnderlined(true).withClickEvent(new ClickEvent.RunCommand("/rai_decide " + command)));
    }

    private void replace(Quick quick, Component line) {
        var chat = client.gui.hud.getChat();
        if (!((ChatHistory) chat).rai$replace(quick.status, line)) chat.addClientSystemMessage(line);
        quick.status = line;
    }

    private void local(Component text) { if (client != null) client.gui.hud.getChat().addClientSystemMessage(text); }
    static String text(JsonObject value, String key) { return value.has(key) && !value.get(key).isJsonNull() ? value.get(key).getAsString() : ""; }
    static String errorText(JsonObject value) { String message = text(value, "message"); return message.isBlank() ? text(value, "code") : message; }
    static boolean terminal(String status) { return Set.of("completed", "canceled", "failed", "incomplete", "interrupted", "refused", "unreadable").contains(status); }
    private static String id() { return UUID.randomUUID().toString().replace("-", ""); }
    private static String failureCode(Throwable error) {
        while (error instanceof CompletionException && error.getCause() != null) error = error.getCause();
        return error.getMessage() == null ? error.getClass().getSimpleName() : error.getMessage();
    }
    private static Component explain(Throwable error) {
        String code = failureCode(error);
        return switch (code) {
            case "GAME_AI_BRIDGE_REQUIRED", "GAME_AI_BRIDGE_UNAVAILABLE", "GAME_BRIDGE_DISCONNECTED" -> Component.translatable("rai.error.bridge");
            case "AI_DEFAULT_MODEL_MISSING" -> Component.translatable("rai.error.model");
            case "GAME_AI_MESSAGE_TOO_LARGE" -> Component.translatable("rai.error.large");
            case "GAME_WORLD_STALE", "AI_GAME_WORLD_CHANGED" -> Component.translatable("rai.error.changed");
            default -> Component.translatable("rai.error.request", code);
        };
    }

    private static final class Quick {
        final String id;
        boolean busy;
        long nextRead;
        String waiting;
        String lastStatus;
        JsonObject turn;
        Component status;
        Quick(String id) { this.id = id; }
    }
}
