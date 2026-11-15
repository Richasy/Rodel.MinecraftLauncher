package io.github.richasy.rodel.ai;

import com.google.gson.JsonObject;
import java.util.*;
import net.minecraft.client.gui.GuiGraphics;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ConfirmScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;
import org.lwjgl.glfw.GLFW;

/** Native Forge view of the same save-bound conversation. It owns no model, history, or operation policy. */
final class AiChatScreen extends Screen {
    private final Screen parent;
    private final AiController controller;
    private EditBox input;
    private Button send, stop, approve, reject, retry, older, skip;
    private int left, available, scroll, contentHeight;
    private int revision = -1;
    private String questionId;
    private final List<Button> options = new ArrayList<>();

    AiChatScreen(Screen parent, AiController controller) {
        super(Component.translatable("rai.title")); this.parent = parent; this.controller = controller;
    }

    @Override
    protected void init() {
        options.clear();
        addRenderableWidget(Button.builder(Component.translatable("rai.config.title"), button -> minecraft.setScreen(new AiClientConfig(this)))
            .bounds(width / 2 - 50, 8, 100, 20).build());
        left = Math.max(10, (width - 600) / 2); available = width - left * 2;
        addRenderableWidget(Button.builder(Component.translatable("gui.back"), button -> onClose()).bounds(left, 8, 60, 20).build());
        addRenderableWidget(Button.builder(Component.translatable("rai.new"), button -> minecraft.setScreen(new ConfirmScreen(answer -> {
            minecraft.setScreen(this); if (answer) controller.reset();
        }, Component.translatable("rai.new"), Component.translatable("rai.new.confirm")))).bounds(left + available - 80, 8, 80, 20).build());
        older = addRenderableWidget(Button.builder(Component.translatable("rai.older"), button -> controller.older()).bounds(left, 34, 100, 20).build());
        retry = addRenderableWidget(Button.builder(Component.translatable("rai.retry"), button -> controller.retry()).bounds(left + 104, 34, 70, 20).build());
        skip = addRenderableWidget(Button.builder(Component.translatable("rai.skip"), button -> controller.choose(null, true)).bounds(left + 178, 34, 60, 20).build());
        approve = addRenderableWidget(Button.builder(Component.translatable("rai.approve"), button -> controller.decide(true)).bounds(left, height - 76, 100, 20).build());
        reject = addRenderableWidget(Button.builder(Component.translatable("rai.reject"), button -> controller.decide(false)).bounds(left + 104, height - 76, 70, 20).build());
        input = new EditBox(font, left, height - 28, available - 154, 20, Component.translatable("rai.input"));
        input.setMaxLength(16000); input.setValue(controller.draft()); input.setResponder(controller::draft);
        addRenderableWidget(input);
        stop = addRenderableWidget(Button.builder(Component.translatable("rai.stop"), button -> controller.cancel()).bounds(left + available - 150, height - 28, 70, 20).build());
        send = addRenderableWidget(Button.builder(Component.translatable("rai.send"), button -> submit()).bounds(left + available - 76, height - 28, 76, 20).build());
        setInitialFocus(input); revision = -1;
    }

    private void submit() {
        String message = input.getValue();
        long draftRevision = controller.draftRevision();
        controller.send(message, () -> {
            if (controller.draftRevision() == draftRevision) {
                controller.draft("");
                if (input.getValue().equals(message)) input.setValue("");
            }
            scroll = 0;
        });
    }

    @Override
    public void tick() {
        input.tick();
        JsonObject waiting = controller.waiting();
        boolean question = waiting != null && waiting.has("question");
        boolean confirmation = waiting != null && waiting.has("confirmation");
        boolean enabled = !controller.busy();
        send.active = enabled && (question || controller.modelAvailable()) && !controller.retryPending() && !confirmation
            && (waiting == null || question) && !input.getValue().isBlank();
        send.setMessage(Component.translatable(question ? "rai.answer" : "rai.send"));
        stop.active = waiting != null && enabled;
        approve.visible = reject.visible = confirmation;
        approve.active = reject.active = enabled;
        older.visible = controller.hasOlder(); older.active = enabled;
        skip.visible = question; skip.active = enabled;
        retry.visible = controller.error() != null;
        retry.active = enabled;
        if (revision == controller.revision()) return;
        revision = controller.revision();
        JsonObject prompt = question ? waiting.getAsJsonObject("question") : null;
        String nextQuestion = prompt == null ? null : AiController.text(prompt, "id");
        int count = prompt == null ? 0 : prompt.getAsJsonObject("question").getAsJsonArray("options").size();
        // Polling updates the controller revision. Rebuilding identical options would discard keyboard focus every read.
        if (Objects.equals(questionId, nextQuestion) && options.size() == count) return;
        var focused = getFocused();
        for (Button option : options) removeWidget(option);
        options.clear();
        questionId = nextQuestion;
        if (focused != null && children().contains(focused)) setFocused(focused);
        else setFocused(input);
        if (prompt == null) return;
        JsonObject definition = prompt.getAsJsonObject("question");
        var values = definition.getAsJsonArray("options");
        for (var option : values) {
            JsonObject value = option.getAsJsonObject();
            String id = AiController.text(value, "id");
            Button button = Button.builder(Component.literal(AiController.text(value, "label")), widget -> {
                controller.choose(id, false);
            }).bounds(left, 0, available, 20).build();
            options.add(addRenderableWidget(button));
        }
    }

    @Override
    public boolean keyPressed(int key, int scan, int modifiers) {
        if ((key == GLFW.GLFW_KEY_ENTER || key == GLFW.GLFW_KEY_KP_ENTER) && input.isFocused() && send.active) { submit(); return true; }
        if (key == GLFW.GLFW_KEY_PAGE_UP || key == GLFW.GLFW_KEY_PAGE_DOWN) {
            scroll = Math.max(0, Math.min(Math.max(0, contentHeight - (height - 150)), scroll + (key == GLFW.GLFW_KEY_PAGE_UP ? 1 : -1) * Math.max(20, (height - 150) / 2))); return true;
        }
        return super.keyPressed(key, scan, modifiers);
    }

    @Override
    public boolean mouseScrolled(double mouseX, double mouseY, double delta) {
        scroll = Math.max(0, Math.min(Math.max(0, contentHeight - (height - 156)), scroll + (int) (delta * 24))); return true;
    }

    @Override
    public void render(GuiGraphics graphics, int mouseX, int mouseY, float delta) {
        renderBackground(graphics);
        boolean dark = AiClientConfig.dark();
        graphics.fill(left, 54, left + available, height - 36, dark ? 0xF5182225 : 0xF5FFFCEF);
        graphics.drawString(font, font.plainSubstrByWidth(controller.worldName() + " · " + controller.model(), available), left, 58, (AiClientConfig.dark() ? 0xb9c6b9 : 0x574a34), false);
        int top = 74, bottom = height - 88;
        List<Line> lines = new ArrayList<>();
        if (!controller.loaded()) append(lines, Component.translatable("rai.loading").getString(), (AiClientConfig.dark() ? 0xb9c6b9 : 0x574a34));
        else if (!controller.modelAvailable()) append(lines, Component.translatable("rai.error.model").getString(), 0xffdf9c);
        else if (controller.turns().isEmpty()) {
            append(lines, Component.translatable("rai.empty.title").getString(), (AiClientConfig.dark() ? 0xffffff : 0x30291e));
            append(lines, Component.translatable("rai.empty.description").getString(), (AiClientConfig.dark() ? 0xb9c6b9 : 0x574a34));
        }
        List<Integer> optionLines = new ArrayList<>();
        for (JsonObject turn : controller.turns()) {
            append(lines, Component.translatable("rai.you").getString() + ": " + AiController.text(turn, "prompt"), 0xb2d3a8);
            append(lines, controller.answer(turn), (AiClientConfig.dark() ? 0xffffff : 0x30291e));
            if (turn.has("question")) {
                JsonObject prompt = turn.getAsJsonObject("question");
                JsonObject definition = prompt.getAsJsonObject("question");
                append(lines, AiController.text(definition, "question"), 0xffdf9c);
                if (Objects.equals(questionId, AiController.text(prompt, "id"))) {
                    var values = definition.getAsJsonArray("options");
                    for (int i = 0; i < options.size(); i++) {
                        JsonObject value = values.get(i).getAsJsonObject();
                        options.get(i).setMessage(Component.literal(AiController.text(value, "label")));
                        optionLines.add(lines.size());
                        int rows = (22 + font.lineHeight + 2) / (font.lineHeight + 3);
                        for (int row = 0; row < rows; row++) lines.add(new Line(FormattedCharSequence.EMPTY, (AiClientConfig.dark() ? 0xffffff : 0x30291e)));
                        String description = AiController.text(value, "description");
                        if (!description.isBlank()) append(lines, description, (AiClientConfig.dark() ? 0xb9c6b9 : 0x574a34));
                    }
                }
            }
            if (turn.has("confirmation")) {
                JsonObject confirmation = turn.getAsJsonObject("confirmation");
                append(lines, Component.translatable("rai.confirm", AiController.text(confirmation, "name")).getString(), 0xffdf9c);
                if (confirmation.has("details")) {
                    JsonObject details = confirmation.getAsJsonObject("details");
                    append(lines, AiController.text(details, "text"), (AiClientConfig.dark() ? 0xb9c6b9 : 0x574a34));
                    if (details.has("hasMore") && details.get("hasMore").getAsBoolean()) append(lines, Component.translatable("rai.details.more").getString(), 0xffdf9c);
                }
            }
            if (turn.has("error")) append(lines, AiController.errorText(turn.getAsJsonObject("error")), 0xffb4a8);
            if (!AiController.terminal(AiController.text(turn, "status"))) append(lines, Component.translatable("rai.status." + AiController.text(turn, "status")).getString(), 0xa6b6a6);
            lines.add(new Line(FormattedCharSequence.EMPTY, (AiClientConfig.dark() ? 0xffffff : 0x30291e)));
        }
        contentHeight = lines.size() * (font.lineHeight + 3);
        scroll = Math.max(0, Math.min(Math.max(0, contentHeight - (bottom - top)), scroll));
        // Zero scroll follows the newest text; upward scrolling reveals the older prefix.
        int origin = Math.min(top, bottom - contentHeight) + scroll;
        for (int i = 0; i < options.size(); i++) {
            Button button = options.get(i);
            int position = i < optionLines.size() ? origin + optionLines.get(i) * (font.lineHeight + 3) : -100;
            button.setY(position); button.visible = position >= top && position + button.getHeight() <= bottom;
            button.active = !controller.busy();
        }
        graphics.enableScissor(left, top, left + available, Math.max(top, bottom));
        int y = origin;
        for (Line line : lines) { graphics.drawString(font, line.text, left + 4, y, line.color, false); y += font.lineHeight + 3; }
        graphics.disableScissor();
        if (controller.error() != null) graphics.drawString(font, controller.error(), left, height - 48, 0xffb4a8, false);
        super.render(graphics, mouseX, mouseY, delta);
    }

    private void append(List<Line> lines, String text, int color) {
        for (FormattedCharSequence part : font.split(Component.literal(text), Math.max(20, available - 8))) lines.add(new Line(part, color));
    }
    @Override public void onClose() { controller.draft(input.getValue()); minecraft.setScreen(parent); }
    @Override public boolean isPauseScreen() { return true; }
    private record Line(FormattedCharSequence text, int color) { }
}
