package io.github.richasy.rodel.ai;

import com.google.gson.JsonArray;
import com.google.gson.JsonObject;
import net.minecraft.client.gui.Font;
import net.minecraft.locale.Language;
import net.minecraft.network.chat.FormattedText;
import net.minecraft.util.FormattedCharSequence;

/**
 * Review: cached line geometry can hide streaming answers, old-page navigation, approval details or a resource reload.
 * Exercise the actual response commit and native Font with synthetic Core replies. Existing pause/open screenshots
 * contain no model answer and cannot detect stale transcript content. No launcher, model or tool effect is claimed.
 */
public final class AiTranscriptChecks {
    private AiTranscriptChecks() {}

    /** Runs on the client thread with its real font and restores the only replaced global (Language) before returning. */
    public static void verify(Font font) {
        responsesAndEvents(font);
        geometryAndDetails(font);
        languageReload(font);
    }

    private static void responsesAndEvents(Font font) {
        var controller = new AiController();
        var cache = new AiTranscriptLayout();
        controller.apply(snapshot("current", "first answer", "completed"), false);
        var initial = layout(cache, controller, font);
        check(text(initial).contains("first answer"), "Initial answer is absent");
        for (int frame = 0; frame < 100; frame++)
            check(layout(cache, controller, font) == initial, "Unchanged frames repeated transcript layout");

        var streaming = snapshot("current", "", "responding");
        streaming.addProperty("eventTurnId", "current");
        var events = new JsonObject();
        var items = new JsonArray();
        items.add(event(1, "message_started", ""));
        items.add(event(2, "text_delta", "streamed answer"));
        events.add("items", items);
        events.addProperty("nextSequence", 2);
        events.addProperty("hasMore", false);
        streaming.add("events", events);
        controller.apply(streaming, false);
        String shown = text(layout(cache, controller, font));
        check(shown.contains("streamed answer") && !shown.contains("first answer"), "A new delta retained obsolete answer lines");

        controller.apply(snapshot("current", "terminal answer", "completed"), false);
        shown = text(layout(cache, controller, font));
        check(shown.contains("terminal answer") && !shown.contains("streamed answer"), "The final answer did not replace partial text");
        controller.apply(snapshot("older", "earlier answer", "completed"), true);
        var expanded = layout(cache, controller, font);
        check(expanded.turnIds().equals(java.util.List.of("older", "current")), "Reading older history lost its stable turn order");
        check(text(expanded).contains("earlier answer") && text(expanded).contains("terminal answer"), "An older page hid existing content");
    }

    private static void geometryAndDetails(Font font) {
        var controller = new AiController();
        var cache = new AiTranscriptLayout();
        var response = snapshot("review", "A long answer with multiple words. ".repeat(20), "waiting_confirmation");
        var turn = response.getAsJsonArray("turns").get(0).getAsJsonObject();
        var confirmation = new JsonObject();
        var details = new JsonObject();
        details.addProperty("text", "precise review");
        details.addProperty("hasMore", false);
        confirmation.add("details", details);
        turn.add("confirmation", confirmation);
        controller.apply(response.deepCopy(), false);
        var wide = layout(cache, controller, font);
        var narrow = cache.get(controller, font, 70, AiAppearance.Density.NORMAL, AiAppearance.Theme.DARK, false, 0);
        check(narrow.height() > wide.height(), "Narrowing the panel did not rewrap the response");
        var light = cache.get(controller, font, 300, AiAppearance.Density.COMPACT, AiAppearance.Theme.LIGHT, false, 0);
        check(light.lines().stream().anyMatch(line -> line.color() == AiAppearance.Theme.LIGHT.ink() && line.height() == AiAppearance.Density.COMPACT.body()),
            "Changed theme or GUI density retained old line presentation");
        check(!text(light).contains("precise review"), "Collapsed approval details were exposed");
        var expanded = cache.get(controller, font, 300, AiAppearance.Density.COMPACT, AiAppearance.Theme.LIGHT, true, 0);
        check(text(expanded).contains("precise review"), "Expanding approval details retained the collapsed layout");

        var question = new JsonObject();
        question.addProperty("question", "Choose a destination");
        var options = new JsonArray();
        for (String label : java.util.List.of("first", "second")) {
            var option = new JsonObject();
            option.addProperty("label", label);
            option.addProperty("description", label + " destination");
            options.add(option);
        }
        question.add("options", options);
        var prompt = new JsonObject();
        prompt.add("question", question);
        turn.remove("confirmation");
        turn.add("question", prompt);
        controller.apply(response.deepCopy(), false);
        var choices = cache.get(controller, font, 300, AiAppearance.Density.NORMAL, AiAppearance.Theme.DARK, false, 2);
        check(choices.placements().size() == 2 && choices.placements().get(0).optionIndex() == 0 && choices.placements().get(1).optionIndex() == 1,
            "The actual question options lost their ordered hit geometry");
        check(text(choices).contains("first destination") && text(choices).contains("second destination"), "Question descriptions were lost");
    }

    private static void languageReload(Font font) {
        var controller = new AiController();
        var cache = new AiTranscriptLayout();
        controller.apply(snapshot("language", "unchanged answer", "completed"), false);
        var initial = layout(cache, controller, font);
        Language previous = Language.getInstance();
        try {
            Language.inject(new Language() {
                @Override public String getOrDefault(String key, String fallback) { return key.equals("rai.you") ? "Reloaded label" : previous.getOrDefault(key, fallback); }
                @Override public boolean has(String key) { return key.equals("rai.you") || previous.has(key); }
                @Override public boolean isDefaultRightToLeft() { return previous.isDefaultRightToLeft(); }
                @Override public FormattedCharSequence getVisualOrder(FormattedText value) { return previous.getVisualOrder(value); }
            });
            var reloaded = layout(cache, controller, font);
            check(reloaded != initial && text(reloaded).contains("Reloaded label"), "A native language/resource replacement retained old translated lines");
        } finally {
            Language.inject(previous);
        }
    }

    private static AiTranscriptLayout.View layout(AiTranscriptLayout cache, AiController controller, Font font) {
        return cache.get(controller, font, 300, AiAppearance.Density.NORMAL, AiAppearance.Theme.DARK, false, 0);
    }

    private static JsonObject snapshot(String id, String answer, String status) {
        var result = new JsonObject();
        result.addProperty("conversationId", "fixture");
        result.addProperty("modelAvailable", true);
        result.addProperty("modelName", "Controlled response");
        result.addProperty("hasOlder", false);
        var turn = new JsonObject();
        turn.addProperty("id", id);
        turn.addProperty("prompt", "Player question");
        turn.addProperty("answer", answer);
        turn.addProperty("status", status);
        var turns = new JsonArray();
        turns.add(turn);
        result.add("turns", turns);
        return result;
    }

    private static JsonObject event(long sequence, String kind, String text) {
        var result = new JsonObject();
        result.addProperty("sequence", sequence);
        result.addProperty("kind", kind);
        var data = new JsonObject();
        data.addProperty("text", text);
        result.add("data", data);
        return result;
    }

    private static String text(AiTranscriptLayout.View view) {
        var result = new StringBuilder();
        for (var line : view.lines()) {
            line.text().accept((index, style, codePoint) -> { result.appendCodePoint(codePoint); return true; });
            result.append('\n');
        }
        return result.toString();
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
