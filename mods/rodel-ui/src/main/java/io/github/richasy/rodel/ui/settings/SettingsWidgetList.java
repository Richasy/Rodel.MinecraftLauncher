package io.github.richasy.rodel.ui.settings;

import java.util.List;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.SidebarNavigation;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ContainerObjectSelectionList;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.client.input.InputWithModifiers;
import net.minecraft.network.chat.Component;

final class SettingsWidgetList extends ContainerObjectSelectionList<SettingsWidgetList.Row> {
    private final NativeSettingsPage page;
    private final boolean sidebar;

    SettingsWidgetList(NativeSettingsPage page, boolean sidebar) {
        super(Minecraft.getInstance(), 1, 1, 0, 24);
        this.page = page;
        this.sidebar = sidebar;
        centerListVertically = false;
    }

    void setWidgets(List<? extends AbstractWidget> widgets) {
        clearEntries();
        widgets.forEach(widget -> addEntry(new Row(widget)));
    }

    void resize(int x, int y, int width, int height) {
        for (Row row : children()) {
            row.setHeight(sidebar ? page.p(page.density().tight() ? 40 : (page.compact() ? 52 : 56) + 8) : page.rowHeight(row.widget));
        }
        updateSizeAndPosition(width, height, x, y);
    }

    void restoreNavigation(double scroll) {
        for (Row row : children()) {
            if (row.widget instanceof RouteButton route && route.source == page.session.selected) {
                setFocused(row);
                row.setFocused(row.widget);
                break;
            }
        }
        setScrollAmount(scroll * page.scale());
    }

    void revealSelection() {
        for (Row row : children()) {
            if (row.widget instanceof RouteButton route && route.source == page.session.selected) {
                scrollToEntry(row);
                page.rememberSidebar();
                return;
            }
        }
    }

    @Override
    public int getRowWidth() {
        return getWidth() - page.p(sidebar ? 12 : 48)
            + (sidebar ? 2 * net.minecraft.client.gui.components.AbstractSelectionList.Entry.CONTENT_PADDING : 0);
    }

    @Override
    public int getRowLeft() {
        return sidebar ? getX() - net.minecraft.client.gui.components.AbstractSelectionList.Entry.CONTENT_PADDING
            : super.getRowLeft();
    }

    @Override
    protected void extractListBackground(GuiGraphicsExtractor graphics) {}

    @Override
    protected void extractListSeparators(GuiGraphicsExtractor graphics) {}

    @Override
    public int scrollbarWidth() {
        return Math.max(2, page.p(6));
    }

    @Override
    protected void extractScrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        page.scrollbar(graphics, scrollBarX(), getY(), scrollbarWidth(), getHeight(),
            scrollerHeight(), scrollAmount(), maxScrollAmount());
    }

    final class Row extends ContainerObjectSelectionList.Entry<Row> {
        private final AbstractWidget widget;

        Row(AbstractWidget widget) {
            this.widget = widget;
        }

        @Override
        public List<? extends GuiEventListener> children() {
            return List.of(widget);
        }

        @Override
        public List<? extends NarratableEntry> narratables() {
            return List.of(widget);
        }

        @Override
        public void extractContent(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                   boolean hovered, float partialTick) {
            if (sidebar) {
                widget.setRectangle(getContentWidth(), page.p(page.density().tight() ? 32 : page.compact() ? 52 : 56), getContentX(), getContentY());
            } else {
                page.row(graphics, widget, getContentX(), getContentY(), getContentWidth(), mouseX, mouseY);
            }
            widget.extractRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }

    static final class RouteButton extends Button {
        private final NativeSettingsPage page;
        private final Button source;
        private final boolean utility;

        RouteButton(NativeSettingsPage page, Button source) {
            this(page, source, false);
        }

        RouteButton(NativeSettingsPage page, Button source, boolean utility) {
            super(0, 0, 1, 1, source == null ? page.session.root.getTitle() : source.getMessage(),
                button -> {}, DEFAULT_NARRATION);
            this.page = page;
            this.source = source;
            this.utility = utility;
            active = source == null || source.active;
        }

        @Override
        public void onPress(InputWithModifiers input) {
            if (source != null && !source.active) return;
            page.session.navigate(source, input);
        }

        @Override
        protected void extractContents(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
            active = source == null || source.active;
            setMessage(source == null ? page.session.root.getTitle() : source.getMessage());
            MenuTheme theme = page.theme();
            boolean selected = page.session.selected == source;
            boolean focused = isFocused() && page.focusVisible() && (utility || page.navigationFocused());
            if (!utility) {
                SidebarNavigation.render(graphics, page, this, selected, focused);
                return;
            }
            int ink = active ? selected ? theme.light() ? 0xFF463116 : theme.ink() : theme.muted() : theme.edge();
            if (utility) {
                graphics.pose().pushMatrix();
                graphics.pose().translate(getX(), getY());
                graphics.pose().scale(page.scale());
                ink = theme.button(graphics, Math.round(getWidth() / page.scale()),
                    Math.round(getHeight() / page.scale()), false, active && (isHovered() || focused), false);
                graphics.pose().popMatrix();
            }
            if (focused) {
                graphics.outline(getX(), getY(), getWidth(), getHeight(), theme.focus());
            }
            Component label = MenuText.styled(SettingsText.label(getMessage()));
            Component fullLabel = label;
            float fontSize = utility ? page.compact() ? 12 : 14 : page.compact() ? 15 : 18;
            float textScale = fontSize / Minecraft.getInstance().font.lineHeight * page.scale();
            int available = getWidth() - page.p(28);
            if (Minecraft.getInstance().font.width(label) * textScale > available) {
                int ellipsis = Minecraft.getInstance().font.width("…");
                String shortened = Minecraft.getInstance().font.substrByWidth(label,
                    Math.max(1, (int) (available / textScale) - ellipsis)).getString();
                label = MenuText.styled(Component.literal(shortened + "…"));
                if (isHovered() || focused) page.contextualTooltip(fullLabel);
            }
            MenuText.extract(graphics, Minecraft.getInstance().font, label,
                utility ? getX() + (getWidth() - Minecraft.getInstance().font.width(label) * textScale) / 2 : getX() + page.p(14),
                getY() + (getHeight() - Minecraft.getInstance().font.lineHeight * textScale) / 2,
                Minecraft.getInstance().font.lineHeight * textScale,
                ink);
        }
    }
}
