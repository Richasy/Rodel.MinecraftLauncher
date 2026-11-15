package io.github.richasy.rodel.ai;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.AbstractScrollArea;
import net.minecraft.client.gui.components.MultiLineEditBox;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.PreeditEvent;
import net.minecraft.network.chat.Component;

/** Keeps the native multiline editor, selection, scrolling and IME in the same GUI transform. */
final class NativeComposer extends AbstractWidget {
    private final AiAppearance appearance;
    private final MultiLineEditBox editor;
    private boolean composing;

    NativeComposer(AiAppearance appearance, MultiLineEditBox editor) {
        super(0, 0, 1, 1, Component.translatable("rai.input"));
        this.appearance = appearance;
        this.editor = editor;
    }

    int desiredHeight() { return Math.clamp(editor.getInnerHeight() + 8, 26, 44); }

    @Override public void setRectangle(int width, int height, int x, int y) {
        super.setRectangle(width, height, x, y);
        editor.setRectangle(Math.max(1, width - AbstractScrollArea.SCROLLBAR_WIDTH - 10), Math.max(1, height - 8), x + 4, y + 4);
        editor.refreshScrollAmount();
    }

    @Override
    protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
        var theme = appearance.theme();
        graphics.fill(getX(), getY(), getRight(), getBottom(), theme.surface());
        graphics.outline(getX(), getY(), getWidth(), getHeight(), isFocused() && appearance.focusVisible() ? theme.focus() : theme.edge());
        editor.extractRenderState(graphics, mouseX, mouseY, tick);
        if (editor.getValue().isEmpty() && !composing) {
            var font = Minecraft.getInstance().font;
            var hint = AiText.fitText(font, AiText.regularText(Component.translatable("rai.input")), appearance.density().body(), getWidth() - 10);
            AiText.drawText(graphics, hint, getX() + 5, getY() + 5, theme.muted());
        }
    }

    @Override public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) { return active && isMouseOver(event.x(), event.y()) && editor.mouseClicked(event, doubleClick); }
    @Override public boolean mouseDragged(MouseButtonEvent event, double x, double y) { return active && editor.mouseDragged(event, x, y); }
    @Override public boolean mouseReleased(MouseButtonEvent event) { return editor.mouseReleased(event); }
    @Override public boolean mouseScrolled(double x, double y, double horizontal, double vertical) { return active && editor.mouseScrolled(x, y, horizontal, vertical); }
    @Override public boolean keyPressed(KeyEvent event) { return active && editor.keyPressed(event); }
    @Override public boolean keyReleased(KeyEvent event) { return active && editor.keyReleased(event); }
    @Override public boolean charTyped(CharacterEvent event) { return active && editor.charTyped(event); }
    @Override public boolean preeditUpdated(PreeditEvent event) { composing = event != null && !event.fullText().isEmpty(); return active && editor.preeditUpdated(event); }
    @Override public boolean capturesInput() { return active && editor.capturesInput(); }
    @Override public void setFocused(boolean focused) { super.setFocused(focused); editor.setFocused(focused); }
    @Override protected void updateWidgetNarration(NarrationElementOutput output) { editor.updateNarration(output); }
}
