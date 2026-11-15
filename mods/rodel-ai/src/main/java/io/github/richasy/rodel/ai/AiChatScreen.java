package io.github.richasy.rodel.ai;

import java.util.ArrayList;
import java.util.List;
import com.google.gson.JsonObject;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.AbstractScrollArea;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.MultiLineEditBox;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.screens.ConfirmScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import com.mojang.blaze3d.platform.InputConstants;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.PreeditEvent;

/** A reading-oriented side panel over the real world, using its own palette and native input/IME. */
public final class AiChatScreen extends Screen {
    private final Screen parent;
    private final AiController controller;
    private final AiTranscriptLayout transcriptLayout = new AiTranscriptLayout();
    private final AiAppearance appearance = new AiAppearance();
    private MultiLineEditBox editor;
    private NativeComposer input;
    private AiButton send, stop, approve, reject, retry, older, details, skip, newChat, latest;
    private int left, top, panelWidth, panelHeight, inset, bodyTop, bodyBottom, footer, approvalTop;
    private int renderMouseX, renderMouseY;
    private double scroll;
    private int contentHeight;
    private boolean followTail = true;
    private boolean showDetails;
    private final List<AiButton> optionButtons = new ArrayList<>();
    private String questionId;
    private boolean composing;
    private boolean draggingScrollbar;
    private double scrollbarGrab;
    private int previousContentHeight;
    private String firstVisibleTurn;

    AiChatScreen(Screen parent, AiController controller) {
        super(Component.translatable("rai.title"));
        this.parent = parent;
        this.controller = controller;
    }

    /** Returns the AI editor's own text color for its scoped native palette mixin. */
    public int textColor() { return appearance.theme().ink(); }
    @Override public boolean isPauseScreen() { return true; }
    @Override public boolean isInGameUi() { return true; }

    @Override
    protected void init() {
        transcriptLayout.clear();
        optionButtons.clear();
        var density = appearance.density();
        inset = density.inset() + 2;
        int control = density.control(), gap = density.gap();
        top = Math.min(8, Math.max(2, height / 30));
        panelWidth = Math.min(width - top * 2, Math.max(240, Math.min(360, Math.round(width * .5f))));
        left = width - panelWidth - top;
        panelHeight = height - top * 2;
        int x = left + inset, right = left + panelWidth - inset, header = top + inset;
        int newWidth = buttonWidth("rai.new");
        var settings = addRenderableWidget(new AiButton(right - control * 2 - gap * 2 - newWidth, header, control, control, "rai.config.title", false,
            () -> minecraft.gui.setScreen(new AiClientConfig(this))).quiet());
        settings.icon = "⚙";
        settings.setTooltip(Tooltip.create(settings.getMessage()));
        newChat = addRenderableWidget(new AiButton(right - control - gap - newWidth, header, newWidth, control, "rai.new", false, () -> {
            if (controller.turns().isEmpty()) controller.reset();
            else minecraft.gui.setScreen(new ConfirmScreen(confirmed -> {
                minecraft.gui.setScreen(this);
                if (confirmed) { followTail = true; controller.reset(); }
            }, Component.translatable("rai.new"), Component.translatable("rai.new.confirm")));
        }).quiet());
        var close = addRenderableWidget(new AiButton(right - control, header, control, control, "rai.close.panel", false, this::onClose).quiet());
        close.icon = "×";
        close.setTooltip(Tooltip.create(close.getMessage()));
        footer = top + panelHeight - inset - control;
        int sendWidth = Math.max(48, Math.max(buttonWidth("rai.send"), buttonWidth("rai.answer")));
        send = addRenderableWidget(new AiButton(right - sendWidth, footer, sendWidth, control, "rai.send", true, this::send));
        stop = addRenderableWidget(new AiButton(right - sendWidth, footer, sendWidth, control, "rai.stop", false, controller::cancel));
        skip = addRenderableWidget(new AiButton(x, footer, buttonWidth("rai.skip"), control, "rai.skip", false, () -> controller.choose(null, true)).quiet());
        retry = addRenderableWidget(new AiButton(x, footer, buttonWidth("rai.retry"), control, "rai.retry", false, controller::retry).quiet());
        approve = addRenderableWidget(new AiButton(x + gap, 0, buttonWidth("rai.approve"), control, "rai.approve", true, () -> controller.decide(true)));
        reject = addRenderableWidget(new AiButton(0, 0, buttonWidth("rai.reject"), control, "rai.reject", false, () -> controller.decide(false)));
        details = addRenderableWidget(new AiButton(right - gap - buttonWidth("rai.details"), 0, buttonWidth("rai.details"), control, "rai.details", false, () -> showDetails = !showDetails).quiet());
        editor = MultiLineEditBox.builder().setPlaceholder(Component.empty()).setTextColor(appearance.theme().ink())
            .setCursorColor(appearance.theme().ink()).setTextShadow(false).setShowBackground(false).setShowDecorations(false)
            .build(font, Math.max(1, right - x - AbstractScrollArea.SCROLLBAR_WIDTH - 10), 50, Component.translatable("rai.input"));
        editor.setCharacterLimit(64000);
        editor.setValue(controller.draft());
        editor.setValueListener(controller::draft);
        input = addRenderableWidget(new NativeComposer(appearance, editor));
        bodyTop = header + control + density.detail() + gap * 2;
        older = addRenderableWidget(new AiButton(x, bodyTop, right - x - 4, control, "rai.older", false, controller::older).quiet());
        latest = addRenderableWidget(new AiButton(x, 0, buttonWidth("rai.latest"), control, "rai.latest", false, () -> followTail = true).quiet());
        latest.visible = false;
        setInitialFocus(input);
        updateControls();
    }

    private int buttonWidth(String key) {
        return Math.max(appearance.density().control(), (int) Math.ceil(AiText.measureText(font, AiText.regularText(Component.translatable(key)), appearance.density().body()).width()) + 16);
    }

    private void send() {
        String message = editor.getValue();
        if (composing || !send.visible || !send.active || message.isBlank()) return;
        long submittedRevision = controller.draftRevision();
        followTail = true;
        controller.send(message, () -> {
            if (controller.draftRevision() == submittedRevision) {
                controller.draft("");
                if (editor.getValue().equals(message)) editor.setValue("");
            }
        });
    }

    private void updateControls() {
        if (!editor.getValue().equals(controller.draft())) editor.setValue(controller.draft());
        JsonObject waiting = controller.waiting();
        boolean confirmation = waiting != null && waiting.has("confirmation");
        boolean question = waiting != null && waiting.has("question");
        boolean running = waiting != null && !question;
        syncQuestion(question ? waiting.getAsJsonObject("question") : null);
        boolean enabled = !controller.busy();
        newChat.active = enabled;
        send.visible = !running;
        send.active = enabled && (question || controller.modelAvailable()) && !controller.retryPending() && !confirmation
            && (waiting == null || question) && !editor.getValue().isBlank();
        send.setMessage(Component.translatable(question ? "rai.answer" : "rai.send"));
        stop.visible = waiting != null;
        stop.active = enabled;
        approve.visible = reject.visible = details.visible = confirmation;
        approve.active = reject.active = details.active = enabled;
        skip.visible = question && controller.error() == null;
        skip.active = enabled;
        retry.visible = controller.error() != null;
        retry.active = enabled;
        older.visible = controller.hasOlder() && scroll < 1;
        older.active = enabled;
        var density = appearance.density();
        int control = density.control(), gap = density.gap(), x = left + inset, right = left + panelWidth - inset;
        stop.setX(question ? send.getX() - stop.getWidth() - gap : send.getX());
        int inputY = footer - gap - input.desiredHeight();
        input.setRectangle(right - x, input.desiredHeight(), x, inputY);
        input.active = editor.active = true;
        bodyBottom = inputY - gap * 2;
        approvalTop = 0;
        if (confirmation) {
            int bannerHeight = control + density.detail() + gap * 3;
            approvalTop = inputY - gap - bannerHeight;
            int buttonsY = approvalTop + bannerHeight - gap - control;
            approve.setY(buttonsY); reject.setPosition(approve.getRight() + 4, buttonsY); details.setY(buttonsY);
            bodyBottom = approvalTop - gap;
        }
        latest.visible = !followTail && contentHeight > bodyBottom - bodyTop;
        if (latest.visible) {
            latest.setPosition(x + (right - x - latest.getWidth()) / 2, bodyBottom - control);
            bodyBottom -= control + gap;
        }
        var focused = getFocused();
        if (focused != null && (!children().contains(focused) || focused instanceof AbstractWidget widget && (!widget.visible || !widget.active)))
            setFocused(input);
    }

    private void syncQuestion(JsonObject prompt) {
        String id = prompt == null ? null : AiController.text(prompt, "id");
        JsonObject question = prompt == null ? null : prompt.getAsJsonObject("question");
        int count = question == null ? 0 : question.getAsJsonArray("options").size();
        if (java.util.Objects.equals(questionId, id) && optionButtons.size() == count) return;
        for (var button : optionButtons) removeWidget(button);
        optionButtons.clear(); questionId = id;
        if (question == null) return;
        for (var item : question.getAsJsonArray("options")) {
            var option = item.getAsJsonObject();
            String optionId = AiController.text(option, "id");
            optionButtons.add(addRenderableWidget(new AiButton(0, 0, 1, appearance.density().control(), "rai.answer", false, () -> {
                controller.choose(optionId, false);
            })));
        }
        updateOptionLabels(question);
    }

    private void updateOptionLabels(JsonObject question) {
        for (int i = 0; i < optionButtons.size(); i++) {
            var option = question.getAsJsonArray("options").get(i).getAsJsonObject();
            optionButtons.get(i).setMessage(Component.literal(AiController.text(option, "label")));
        }
    }


    @Override
    public void extractRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        renderMouseX = mouseX; renderMouseY = mouseY;
        updateControls();
        var theme = appearance.theme();
        var density = appearance.density();
        int x = left + inset, available = panelWidth - inset * 2 - 5;
        theme.panel(graphics, left, top, panelWidth, panelHeight);
        text(graphics, getTitle(), x, top + inset + 2, density.heading(), newChat.getX() - x - density.control() - density.gap() * 2, theme.ink());
        int contextY = top + inset + density.control() + 3;
        String model = controller.loaded() ? controller.model() : Component.translatable("rai.status.connecting").getString();
        int modelWidth = Math.min(available * 2 / 5, (int) Math.ceil(AiText.measureText(font, AiText.regularText(Component.literal(model)), density.detail()).width()) + 1);
        Component pauseStatus = Component.translatable(minecraft.isPaused() ? "rai.paused" : "rai.unpaused");
        int pauseWidth = Math.min(available / 3, (int) Math.ceil(AiText.measureText(font, AiText.regularText(pauseStatus), density.detail()).width()) + 1);
        int pauseX = x + available - modelWidth - density.gap() - pauseWidth;
        text(graphics, Component.literal(controller.worldName()), x, contextY, density.detail(), pauseX - x - density.gap(), theme.muted());
        text(graphics, pauseStatus, pauseX, contextY, density.detail(), pauseWidth, theme.muted());
        text(graphics, Component.literal(model), x + available - modelWidth, contextY, density.detail(), modelWidth, theme.muted());
        graphics.fill(x, bodyTop - density.gap(), x + available, bodyTop - density.gap() + 1, theme.edge());
        graphics.fill(x, input.getY() - density.gap(), x + available, input.getY() - density.gap() + 1, theme.edge());
        if (approvalTop > 0) {
            graphics.fill(x, approvalTop, x + available + 5, input.getY() - density.gap(), theme.hover());
            var pending = controller.waiting().getAsJsonObject("confirmation");
            text(graphics, Component.translatable("rai.confirm", AiController.text(pending, "name")), x + density.gap(), approvalTop + density.gap(), density.detail(), available - density.gap() * 2, theme.ink());
        }
        if (!retry.visible && !skip.visible) text(graphics, Component.translatable(controller.waiting() == null ? "rai.input.help" : "rai.input.waiting"), x, footer + (density.control() - density.detail()) / 2, density.detail(), send.getX() - x - density.gap(), theme.muted());

        var transcript = transcriptLayout.get(controller, font, available, density, theme, showDetails, optionButtons.size());
        optionButtons.forEach(button -> button.visible = false);
        contentHeight = transcript.height();
        int viewport = Math.max(1, bodyBottom - bodyTop);
        if (followTail) scroll = Math.max(0, contentHeight - viewport);
        else if (firstVisibleTurn != null && !transcript.turnIds().isEmpty()
            && !firstVisibleTurn.equals(transcript.turnIds().getFirst())
            && transcript.turnIds().contains(firstVisibleTurn)) scroll += contentHeight - previousContentHeight;
        firstVisibleTurn = transcript.turnIds().isEmpty() ? null : transcript.turnIds().getFirst();
        previousContentHeight = contentHeight;
        scroll = Math.clamp(scroll, 0, Math.max(0, contentHeight - viewport));
        for (var placement : transcript.placements()) {
            var button = optionButtons.get(placement.optionIndex());
            button.setRectangle(placement.width(), density.control(), x + placement.x(), bodyTop + placement.y() - (int) scroll);
            button.visible = button.getY() >= bodyTop && button.getBottom() <= bodyBottom;
            button.active = !controller.busy();
        }
        graphics.enableScissor(left + 1, bodyTop, left + panelWidth - 1, bodyBottom);
        for (var surface : transcript.surfaces()) {
            int sy = bodyTop + surface.y() - (int) scroll;
            graphics.fill(x + surface.x(), sy, x + surface.x() + surface.width(), sy + surface.height(), theme.hover());
        }
        for (var line : transcript.lines()) {
            float lineY = bodyTop + line.y() - (float) scroll;
            if (lineY + line.height() < bodyTop || lineY > bodyBottom) continue;
            graphics.pose().pushMatrix();
            graphics.pose().translate(x + line.x(), lineY);
            graphics.pose().scale(line.height() / (float) font.lineHeight);
            graphics.text(font, line.text(), 0, 0, line.color(), false);
            graphics.pose().popMatrix();
        }
        graphics.disableScissor();
        if (contentHeight > viewport) {
            int thumb = Math.max(10, viewport * viewport / contentHeight);
            int thumbY = bodyTop + (int) ((viewport - thumb) * scroll / Math.max(1, contentHeight - viewport));
            graphics.fill(left + panelWidth - 3, thumbY, left + panelWidth - 1, thumbY + thumb, theme.muted());
        }
        super.extractRenderState(graphics, mouseX, mouseY, tick);
    }

    private void text(GuiGraphicsExtractor graphics, Component text, int x, int y, int size, int width, int color) {
        var layout = AiText.fitText(font, AiText.regularText(text), size, Math.max(0, width));
        AiText.drawText(graphics, layout, x, y, color);
        if (layout.trimmed() && renderMouseX >= x && renderMouseX < x + width && renderMouseY >= y && renderMouseY < y + layout.height())
            graphics.setTooltipForNextFrame(text, renderMouseX, renderMouseY);
    }

    @Override
    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (x >= left && x < left + panelWidth && y >= bodyTop && y <= bodyBottom) {
            scroll -= vertical * appearance.density().row();
            scroll = Math.clamp(scroll, 0, Math.max(0, contentHeight - bodyBottom + bodyTop));
            followTail = scroll >= contentHeight - bodyBottom + bodyTop - 2;
            return true;
        }
        return super.mouseScrolled(x, y, horizontal, vertical);
    }

    @Override
    public boolean keyPressed(KeyEvent event) {
        if (getFocused() == input && !composing && !event.hasShiftDown() && (event.key() == InputConstants.KEY_RETURN || event.key() == InputConstants.KEY_NUMPADENTER)) { send(); return true; }
        if (getFocused() != input && (event.key() == InputConstants.KEY_PAGEUP || event.key() == InputConstants.KEY_PAGEDOWN)) {
            mouseScrolled(left + inset, bodyTop + 1, 0, event.key() == InputConstants.KEY_PAGEUP ? 3 : -3); return true;
        }
        return super.keyPressed(event);
    }

    @Override public boolean preeditUpdated(PreeditEvent event) { composing = event != null && !event.fullText().isEmpty(); return super.preeditUpdated(event); }

    @Override
    public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        int viewport = bodyBottom - bodyTop;
        if (event.button() == InputConstants.MOUSE_BUTTON_LEFT && contentHeight > viewport && event.x() >= left + panelWidth - 7 && event.x() < left + panelWidth
            && event.y() >= bodyTop && event.y() < bodyBottom) {
            int thumb = Math.max(10, viewport * viewport / contentHeight);
            double thumbY = bodyTop + (viewport - thumb) * scroll / Math.max(1, contentHeight - viewport);
            scrollbarGrab = event.y() >= thumbY && event.y() < thumbY + thumb ? event.y() - thumbY : thumb / 2.0;
            draggingScrollbar = true; dragScrollbar(event.y()); return true;
        }
        return super.mouseClicked(event, doubleClick);
    }

    private void dragScrollbar(double y) {
        int viewport = bodyBottom - bodyTop;
        int thumb = Math.max(10, viewport * viewport / Math.max(1, contentHeight));
        scroll = Math.clamp((y - bodyTop - scrollbarGrab) / Math.max(1, viewport - thumb) * (contentHeight - viewport), 0, Math.max(0, contentHeight - viewport));
        followTail = scroll >= contentHeight - viewport - 2;
    }

    @Override public boolean mouseDragged(MouseButtonEvent event, double x, double y) { if (draggingScrollbar) { dragScrollbar(event.y()); return true; } return super.mouseDragged(event, x, y); }
    @Override public boolean mouseReleased(MouseButtonEvent event) { if (draggingScrollbar) { draggingScrollbar = false; return true; } return super.mouseReleased(event); }
    @Override public void removed() { transcriptLayout.clear(); super.removed(); }
    @Override public void onClose() { controller.draft(editor.getValue()); minecraft.gui.setScreen(parent); }


    private final class AiButton extends Button {
        private final boolean accent;
        private boolean quiet;
        private String icon;
        AiButton(int x, int y, int width, int height, String key, boolean accent, Runnable action) {
            super(x, y, width, height, Component.translatable(key), button -> action.run(), DEFAULT_NARRATION);
            this.accent = accent;
        }
        AiButton quiet() { quiet = true; return this; }
        @Override
        protected void extractContents(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
            boolean highlighted = isHovered() || isFocused() && appearance.focusVisible();
            boolean down = highlighted && (isHovered() && minecraft.mouseHandler.isLeftPressed()
                || isFocused() && (InputConstants.isKeyDown(InputConstants.KEY_RETURN) || InputConstants.isKeyDown(InputConstants.KEY_NUMPADENTER) || InputConstants.isKeyDown(InputConstants.KEY_SPACE)));
            var theme = appearance.theme();
            if (!quiet || highlighted) {
                int background = !active ? 0xFF707570 : accent ? 0xFF416C46 : down ? theme.edge() : highlighted ? theme.hover() : theme.surface();
                graphics.fill(getX(), getY(), getRight(), getBottom(), background);
                graphics.outline(getX(), getY(), getWidth(), getHeight(), theme.edge());
            }
            int color = !active ? theme.muted() : accent ? 0xFFFFFFFF : quiet && !highlighted ? theme.muted() : theme.ink();
            var label = icon == null ? getMessage() : Component.literal(icon);
            var layout = AiText.fitText(font, AiText.regularText(label), appearance.density().body(), Math.max(0, getWidth() - 8));
            AiText.drawText(graphics, layout, getX() + (getWidth() - layout.width()) / 2, getY() + (getHeight() - layout.height()) / 2, color);
            if (icon == null && layout.trimmed() && isHovered()) graphics.setTooltipForNextFrame(getMessage(), mouseX, mouseY);
            if (isFocused() && appearance.focusVisible()) graphics.outline(getX() - 1, getY() - 1, getWidth() + 2, getHeight() + 2, appearance.theme().focus());
        }
    }
}
