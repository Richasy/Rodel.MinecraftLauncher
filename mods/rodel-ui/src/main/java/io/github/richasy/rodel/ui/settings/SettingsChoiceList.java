package io.github.richasy.rodel.ui.settings;

import java.util.List;
import com.mojang.blaze3d.platform.InputConstants;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ContainerObjectSelectionList;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.client.input.MouseButtonEvent;

public final class SettingsChoiceList extends ContainerObjectSelectionList<SettingsChoiceList.Row> {
    private final NativeFormContext page;
    private int selected;

    public SettingsChoiceList(NativeFormContext page) {
        super(Minecraft.getInstance(), 1, 1, 0, 24);
        this.page = page;
        centerListVertically = false;
        visible = false;
    }

    public void open(AbstractWidget source, int screenWidth, int screenHeight) {
        open(source, screenWidth, screenHeight, ((NativeCycleChoices) source).rodel$choices());
    }

    public void open(AbstractWidget source, int screenWidth, int screenHeight, List<NativeCycleChoices.Choice> choices) {
        clearEntries();
        if (choices.isEmpty()) throw new IllegalStateException("Native option has no available choices");
        var font = Minecraft.getInstance().font;
        int size = page.controlTextSize();
        int width = source.getWidth();
        if (!(source instanceof net.minecraft.client.gui.components.CycleButton<?>))
            for (var choice : choices) width = Math.max(width, page.p(48 + font.width(MenuText.styled(choice.label())) * (float) size / font.lineHeight));
        width = Math.min(width, screenWidth - page.p(32));
        int content = 0;
        selected = 0;
        for (int i = 0; i < choices.size(); i++) {
            var choice = choices.get(i);
            int lines = font.split(MenuText.styled(choice.label()),
                Math.max(1, Math.round((width - page.p(48)) * font.lineHeight / (size * page.scale())))).size();
            Row row = new Row(choice);
            row.setHeight(page.p((page.density().tight() ? 8 : 16) + Math.max(1, lines) * size * 1.5f));
            addEntry(row, row.getHeight());
            content += row.getHeight();
            if (choice.selected()) selected = i;
        }
        int below = screenHeight - source.getBottom() - page.p(20);
        int above = source.getY() - page.p(20);
        int height = Math.min(content + page.p(8), Math.min(page.p(320), Math.max(above, below)));
        int x = Math.max(page.p(16), Math.min(source.getRight() - width, screenWidth - width - page.p(16)));
        int y = below >= height ? source.getBottom() + page.p(4) : source.getY() - height - page.p(4);
        updateSizeAndPosition(width, height, x, y);
        visible = true;
        focusRow(selected);
    }

    public void close() {
        visible = false;
        clearEntries();
    }

    private void focusRow(int index) {
        selected = Math.max(0, Math.min(children().size() - 1, index));
        Row row = children().get(selected);
        setFocused(row);
        row.setFocused(row.button);
    }

    @Override
    public boolean keyPressed(KeyEvent event) {
        if (!visible) return false;
        if (event.key() == InputConstants.KEY_ESCAPE || event.key() == InputConstants.KEY_TAB) close();
        else if (event.key() == InputConstants.KEY_UP) focusRow(selected - 1);
        else if (event.key() == InputConstants.KEY_DOWN) focusRow(selected + 1);
        else if (event.key() == InputConstants.KEY_RETURN || event.key() == InputConstants.KEY_SPACE) {
            children().get(selected).button.onPress(event);
        }
        return true;
    }

    @Override
    public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        if (!visible) return false;
        if (!isMouseOver(event.x(), event.y())) close();
        else super.mouseClicked(event, doubleClick);
        return true;
    }

    @Override
    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (!visible) return false;
        super.mouseScrolled(x, y, horizontal, vertical);
        return true;
    }

    @Override
    public void extractWidgetRenderState(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {}

    public void overlay(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        if (!visible) return;
        graphics.nextStratum();
        page.theme().panel(graphics, getX(), getY(), getWidth(), getHeight());
        graphics.fill(getX() + 1, getY() + 1, getRight() - 1, getBottom() - 1,
            page.theme().surface() | 0xFF000000);
        super.extractWidgetRenderState(graphics, mouseX, mouseY, partialTick);
    }

    @Override
    public int getRowWidth() { return getWidth() - page.p(12); }

    @Override
    protected void extractListBackground(GuiGraphicsExtractor graphics) {}

    @Override
    protected void extractListSeparators(GuiGraphicsExtractor graphics) {}

    @Override
    public int scrollbarWidth() { return Math.max(2, page.p(6)); }

    @Override
    protected void extractScrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        page.scrollbar(graphics, scrollBarX(), getY(), scrollbarWidth(), getHeight(),
            scrollerHeight(), scrollAmount(), maxScrollAmount());
    }

    final class Row extends ContainerObjectSelectionList.Entry<Row> {
        final Button button;

        Row(NativeCycleChoices.Choice choice) {
            button = new Button(0, 0, 1, 1, choice.label(), ignored -> {
                choice.select().run();
                close();
            }, narration -> narration.get()) {
                @Override
                protected void extractContents(GuiGraphicsExtractor graphics, int x, int y, float partialTick) {
                    boolean primaryAction = SettingsText.hasKey(getMessage(), "rodel.worlds.navigation.apply");
                    if (primaryAction) {
                        graphics.fill(getX(), getY(), getRight(), getBottom(), page.theme().primary());
                        graphics.outline(getX(), getY(), getWidth(), getHeight(), page.theme().edge());
                    } else if (choice.selected() || isHovered() || isFocused() && page.focusVisible()) {
                        graphics.fill(getX(), getY(), getRight(), getBottom(), page.theme().hover());
                    }
                    if (isFocused() && page.focusVisible()) graphics.outline(getX(), getY(), getWidth(), getHeight(), page.theme().focus());
                    var font = Minecraft.getInstance().font;
                    int size = page.controlTextSize();
                    var lines = font.split(MenuText.styled(getMessage()),
                        Math.max(1, Math.round((getWidth() - page.p(24)) * font.lineHeight / (size * page.scale()))));
                    int lineHeight = page.p(size * 1.5f);
                    int textSize = page.p(size);
                    float textY = getY() + (getHeight() - lines.size() * lineHeight) / 2f
                        + (lineHeight - textSize) / 2f;
                    for (int i = 0; i < lines.size(); i++) {
                        graphics.pose().pushMatrix();
                        graphics.pose().translate(getX() + page.p(12), textY + i * lineHeight);
                        graphics.pose().scale(textSize / (float) font.lineHeight);
                        graphics.text(font, lines.get(i), 0, 0, primaryAction ? page.theme().primaryInk() : page.theme().ink(), false);
                        graphics.pose().popMatrix();
                    }
                }
            };
        }

        @Override
        public List<? extends GuiEventListener> children() { return List.of(button); }

        @Override
        public List<? extends NarratableEntry> narratables() { return List.of(button); }

        @Override
        public void extractContent(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float partialTick) {
            button.setRectangle(getContentWidth(), getHeight(), getContentX(), getContentY());
            button.extractRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }
}
