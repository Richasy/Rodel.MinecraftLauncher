package io.github.richasy.rodel.ui.worlds;

import java.util.List;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.ContainerObjectSelectionList;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.network.chat.Component;

final class WorldBranchTextList extends ContainerObjectSelectionList<WorldBranchTextList.Row> {
    private final WorldBranchPage page;

    WorldBranchTextList(WorldBranchPage page, List<AbstractWidget> widgets) {
        super(Minecraft.getInstance(), 1, 1, 0, 24);
        this.page = page;
        centerListVertically = false;
        for (int i = 0; i < widgets.size(); i++) addEntry(new Row(widgets.get(i), i == 0));
    }

    void resize(int x, int y, int width, int height) {
        double scroll = scrollAmount();
        for (Row row : children()) row.setHeight(row.height(width - 28));
        updateSizeAndPosition(width, height, x, y);
        setScrollAmount(scroll);
    }

    int naturalHeight(int width) {
        int height = 20;
        for (Row row : children()) height += row.height(width - 28);
        return height;
    }

    @Override public int getRowWidth() { return getWidth() - 24; }
    @Override public int getRowLeft() { return getX() + 10; }
    @Override protected int scrollBarX() { return getRight() - 5; }
    @Override public int scrollbarWidth() { return 3; }
    @Override protected int contentHeight() { return super.contentHeight() + 16; }
    @Override protected void extractListBackground(GuiGraphicsExtractor graphics) {}
    @Override protected void extractListSeparators(GuiGraphicsExtractor graphics) {}
    @Override protected void extractSelection(GuiGraphicsExtractor graphics, Row entry, int color) {}

    final class Row extends ContainerObjectSelectionList.Entry<Row> {
        private final AbstractWidget widget;
        private final boolean title;
        Row(AbstractWidget widget, boolean title) {
            this.widget = widget;
            this.title = title;
        }
        private float textScale() {
            return (title ? page.density().tight() ? page.density().heading() : 12 : page.density().body())
                / (float) Minecraft.getInstance().font.lineHeight;
        }
        private Component presentation() {
            return title ? MenuText.styled(widget.getMessage()) : MenuText.secondary(widget.getMessage());
        }
        int height(int width) {
            if (widget instanceof WorldBranchProgressWidget) return widget.getHeight() + 12;
            var font = Minecraft.getInstance().font;
            return 12 + font.split(presentation(),
                Math.max(1, (int) (width / textScale()))).size() * Math.round(12 * textScale());
        }
        @Override public List<? extends GuiEventListener> children() { return List.of(widget); }
        @Override public List<? extends NarratableEntry> narratables() { return List.of(widget); }
        @Override public void extractContent(GuiGraphicsExtractor graphics, int x, int y, boolean hovered, float partialTick) {
            widget.setPosition(getContentX(), getContentY() + 8);
            if (widget instanceof WorldBranchProgressWidget) {
                widget.setWidth(getContentWidth());
                widget.extractRenderState(graphics, x, y, partialTick);
                return;
            }
            var font = Minecraft.getInstance().font;
            var lines = font.split(presentation(),
                Math.max(1, (int) (getContentWidth() / textScale())));
            graphics.pose().pushMatrix();
            graphics.pose().translate(getContentX(), getContentY() + 8);
            graphics.pose().scale(textScale());
            for (int i = 0; i < lines.size(); i++)
                graphics.text(font, lines.get(i), 0, i * 12,
                    title ? page.theme().ink() : page.theme().muted(), false);
            graphics.pose().popMatrix();
        }
    }
}
