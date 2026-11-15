package io.github.richasy.rodel.ai;

import java.util.ArrayList;
import java.util.List;
import com.google.gson.JsonObject;
import net.minecraft.client.gui.Font;
import net.minecraft.locale.Language;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;

/** One screen's retained transcript geometry; scrolling and live native controls remain owned by the screen. */
final class AiTranscriptLayout {
    private Key key;
    private View view;

    View get(AiController controller, Font font, int width, AiAppearance.Density density, AiAppearance.Theme theme,
             boolean showDetails, int optionCount) {
        // Native language reloads replace Language even when the selected language is unchanged.
        // Include it as well as Font so resource reloads cannot retain obsolete shaping or translations.
        var requested = new Key(controller.revision(), font, Language.getInstance(), width, density, theme, showDetails, optionCount);
        if (!requested.equals(key)) {
            var updated = build(controller, font, width, density, theme, showDetails, optionCount);
            view = updated;
            key = requested;
        }
        return view;
    }

    void clear() {
        key = null;
        view = null;
    }

    private static View build(AiController controller, Font font, int available, AiAppearance.Density density,
                              AiAppearance.Theme theme, boolean showDetails, int optionCount) {
        List<JsonObject> turns = controller.turns();
        List<Line> lines = new ArrayList<>();
        List<Surface> surfaces = new ArrayList<>();
        List<Placement> placements = new ArrayList<>();
        int y = controller.hasOlder() ? density.control() + density.gap() : 0;
        for (JsonObject turn : turns) {
            Component prompt = Component.literal(AiController.text(turn, "prompt"));
            int pad = density.gap();
            int naturalWidth = Math.round(AiText.measureText(font, AiText.regularText(prompt), density.body()).width()) + pad * 2;
            int bubbleWidth = Math.max(available / 3, Math.min(available * 86 / 100, naturalWidth));
            int bubbleX = available - bubbleWidth, start = y;
            y = append(font, lines, Component.translatable("rai.you"), bubbleX + pad, y + pad, bubbleWidth - pad * 2, theme.muted(), density.detail());
            y = append(font, lines, prompt, bubbleX + pad, y + 2, bubbleWidth - pad * 2, theme.ink(), density.body());
            y += pad;
            surfaces.add(new Surface(bubbleX, start, bubbleWidth, y - start));
            y += density.gap() * 2;
            y = append(font, lines, Component.literal("Rodel AI"), 0, y, available, theme.muted(), density.detail());
            y += 2;
            String answer = controller.answer(turn);
            if (!answer.isBlank()) y = append(font, lines, Component.literal(answer), 0, y, available, theme.ink(), density.body());
            if (!turn.has("confirmation") && (!AiController.terminal(AiController.text(turn, "status")) || answer.isBlank()))
                y = append(font, lines, Component.translatable("rai.status." + AiController.text(turn, "status")), 0, y, available, theme.muted(), density.detail());
            if (turn.has("error")) y = append(font, lines, Component.literal(AiController.errorText(turn.getAsJsonObject("error"))), 0, y, available, theme.muted(), density.detail());
            if (turn.has("question")) {
                JsonObject question = turn.getAsJsonObject("question").getAsJsonObject("question");
                y = append(font, lines, Component.literal(AiController.text(question, "question")), 0, y, available, theme.ink(), density.body());
                int optionIndex = 0;
                for (var option : question.getAsJsonArray("options")) {
                    if (optionIndex < optionCount) {
                        placements.add(new Placement(optionIndex++, 0, y, available));
                        y += density.control() + 3;
                    }
                    String description = AiController.text(option.getAsJsonObject(), "description");
                    if (!description.isBlank()) y = append(font, lines, Component.literal(description), 0, y, available, theme.muted(), density.detail());
                }
            }
            if (turn.has("confirmation") && showDetails) {
                JsonObject confirmation = turn.getAsJsonObject("confirmation");
                if (confirmation.has("details")) {
                    JsonObject review = confirmation.getAsJsonObject("details");
                    y = append(font, lines, Component.literal(AiController.text(review, "text")), 0, y, available, theme.muted(), density.detail());
                    if (review.get("hasMore").getAsBoolean()) y = append(font, lines, Component.translatable("rai.details.more"), 0, y, available, theme.muted(), density.detail());
                }
            }
            y += density.gap() * 2;
        }
        if (controller.error() != null) y = append(font, lines, controller.error(), 0, y, available, theme.ink(), density.body());
        if (turns.isEmpty() && controller.error() == null) {
            y += density.gap() * 2;
            if (!controller.loaded()) y = append(font, lines, Component.translatable("rai.loading"), 0, y, available, theme.muted(), density.body());
            else if (!controller.modelAvailable()) y = append(font, lines, Component.translatable("rai.error.model"), 0, y, available, theme.ink(), density.body());
            else {
                y = append(font, lines, Component.translatable("rai.empty.title"), 0, y, available, theme.ink(), density.body() + 2);
                y = append(font, lines, Component.translatable("rai.empty.description"), 0, y + density.gap(), available, theme.muted(), density.body());
            }
        }
        return new View(List.copyOf(lines), List.copyOf(surfaces), List.copyOf(placements),
            turns.stream().map(turn -> AiController.text(turn, "id")).toList(), y);
    }

    private static int append(Font font, List<Line> lines, Component text, int x, int y, int width, int color, int size) {
        float scale = size / (float) font.lineHeight;
        for (FormattedCharSequence line : font.split(AiText.regularText(text), Math.max(1, (int) (width / scale)))) {
            lines.add(new Line(line, x, y, size, color));
            y += size + Math.max(2, size / 3);
        }
        return y;
    }

    record View(List<Line> lines, List<Surface> surfaces, List<Placement> placements, List<String> turnIds, int height) {}
    record Line(FormattedCharSequence text, int x, int y, int height, int color) {}
    record Surface(int x, int y, int width, int height) {}
    record Placement(int optionIndex, int x, int y, int width) {}
    private record Key(int revision, Font font, Language language, int width, AiAppearance.Density density,
                       AiAppearance.Theme theme, boolean showDetails, int optionCount) {}
}
