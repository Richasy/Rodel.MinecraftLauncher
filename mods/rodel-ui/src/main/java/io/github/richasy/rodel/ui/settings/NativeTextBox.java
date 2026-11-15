package io.github.richasy.rodel.ui.settings;

import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.narration.NarrationElementOutput;
import net.minecraft.client.input.CharacterEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.PreeditEvent;
import io.github.richasy.rodel.ui.UiInputGeometry;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.mixin.EditBoxHintAccessor;

public final class NativeTextBox extends AbstractWidget {
    private final MenuContext page;
    private final EditBox editor;

    public NativeTextBox(MenuContext page, EditBox editor) {
        super(editor.getX(), editor.getY(), editor.getWidth(), editor.getHeight(), editor.getMessage());
        this.page = page;
        this.editor = editor;
    }

    public static NativeTextBox search(MenuContext page, EditBox editor) {
        var hint = ((EditBoxHintAccessor) editor).rodel$hint();
        // Pack, language and rules searches use an empty message and a separate native hint.
        if (hint != null && !hint.getString().isBlank()) {
            if (editor.getMessage().getString().isBlank()) editor.setMessage(hint.copy());
        } else if (!editor.getMessage().getString().isBlank()) {
            editor.setHint(editor.getMessage());
        }
        return new NativeTextBox(page, editor);
    }

    private float textScale() {
        return page.density().body() / (float) Minecraft.getInstance().font.lineHeight;
    }

    @Override
    public void setRectangle(int width, int height, int x, int y) {
        super.setRectangle(width, height, x, y);
        prepare();
    }

    private void prepare() {
        editor.setRectangle(Math.round(getWidth() / textScale()), Math.round(getHeight() / textScale()), 0, 0);
    }

    private MouseButtonEvent local(MouseButtonEvent event) {
        return new MouseButtonEvent((event.x() - getX()) / textScale(), (event.y() - getY()) / textScale(), event.buttonInfo());
    }

    @Override
    protected void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        if (!editor.visible) return;
        prepare();
        graphics.pose().pushMatrix();
        graphics.pose().translate(getX(), getY());
        graphics.pose().scale(textScale());
        UiInputGeometry.push(textScale(), getX(), getY());
        try {
            int localX = Math.round((mouseX - getX()) / textScale());
            int localY = Math.round((mouseY - getY()) / textScale());
            editor.extractRenderState(graphics, localX, localY, partialTick);
            UiInputGeometry.flushPreedit(graphics, localX, localY, partialTick);
        } finally {
            UiInputGeometry.pop();
            graphics.pose().popMatrix();
        }
    }

    @Override
    public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        return editor.visible && isMouseOver(event.x(), event.y()) && editor.mouseClicked(local(event), doubleClick);
    }

    @Override public boolean isActive() { return editor.isActive(); }

    @Override
    public boolean mouseDragged(MouseButtonEvent event, double x, double y) {
        return editor.mouseDragged(local(event), x / textScale(), y / textScale());
    }

    @Override
    public boolean mouseReleased(MouseButtonEvent event) { return editor.mouseReleased(local(event)); }

    @Override
    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        return editor.mouseScrolled((x - getX()) / textScale(), (y - getY()) / textScale(), horizontal, vertical);
    }

    @Override
    public boolean keyPressed(KeyEvent event) { return editor.keyPressed(event); }

    @Override
    public boolean keyReleased(KeyEvent event) { return editor.keyReleased(event); }

    @Override
    public boolean charTyped(CharacterEvent event) { return editor.charTyped(event); }

    @Override
    public boolean preeditUpdated(PreeditEvent event) { return editor.preeditUpdated(event); }

    @Override
    public boolean capturesInput() { return editor.capturesInput(); }

    @Override
    public void setFocused(boolean focused) {
        super.setFocused(focused);
        editor.setFocused(focused);
    }

    @Override
    protected void updateWidgetNarration(NarrationElementOutput output) { editor.updateNarration(output); }

    public List<EditBox> children() { return List.of(editor); }

    public EditBox editor() { return editor; }
}
