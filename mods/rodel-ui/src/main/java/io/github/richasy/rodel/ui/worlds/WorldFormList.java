package io.github.richasy.rodel.ui.worlds;

import java.util.ArrayList;
import java.util.List;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import io.github.richasy.rodel.ui.settings.SettingsText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractButton;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ContainerObjectSelectionList;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.MultiLineTextWidget;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.narration.NarratableEntry;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.TextColor;
import net.minecraft.util.FormattedCharSequence;

final class WorldFormList extends ContainerObjectSelectionList<WorldFormList.Row> {
    private final WorldSurface page;
    private final boolean more;

    WorldFormList(WorldSurface page, Tab tab) {
        this(page, SettingsText.hasKey(tab.getTabTitle(), "createWorld.tab.more.title"), widgets(tab));
    }

    WorldFormList(WorldSurface page, List<AbstractWidget> widgets) {
        this(page, false, widgets);
    }

    private static List<AbstractWidget> widgets(Tab tab) {
        List<AbstractWidget> widgets = new ArrayList<>();
        tab.visitChildren(widgets::add);
        return widgets;
    }

    private WorldFormList(WorldSurface page, boolean more, List<AbstractWidget> widgets) {
        super(Minecraft.getInstance(), 1, 1, 0, 44);
        this.page = page;
        this.more = more;
        centerListVertically = false;
        List<Row> rows = new ArrayList<>();
        Component previousLabel = Component.empty();
        for (AbstractWidget widget : widgets) {
            if (widget instanceof MultiLineTextWidget && !rows.isEmpty()) {
                rows.getLast().inlineDescription = widget.getMessage().copy().withStyle(style -> style.withColor((TextColor) null));
                continue;
            }
            if (widget instanceof StringWidget) {
                previousLabel = widget.getMessage();
                continue;
            }
            if (!(widget instanceof EditBox) && !(widget instanceof AbstractButton)) {
                throw new IllegalStateException("Unsupported native world field: " + widget.getClass().getName());
            }
            Component caption = widget instanceof NativeOptionPresentation presentation ? presentation.rodel$caption() : widget.getMessage();
            if (caption.getString().isBlank()) caption = previousLabel;
            if (widget instanceof AbstractButton) ((ThemedButton) widget).rodel$setStyle(page,
                !more ? null : SettingsText.hasKey(caption, "selectWorld.dataPacks") ? MenuIcon.PACK
                    : SettingsText.hasKey(caption, "selectWorld.experiments") ? MenuIcon.FLASK : MenuIcon.SETTINGS,
                false, false, more);
            if (widget instanceof Button button && SettingsText.hasKey(caption, "selectWorld.customizeType")
                && !rows.isEmpty() && rows.getLast().widget instanceof CycleButton<?>) {
                rows.getLast().accessory = button;
            } else rows.add(new Row(widget, caption));
            previousLabel = Component.empty();
        }
        rows.sort(java.util.Comparator.comparing(row -> !(row.widget instanceof EditBox)));
        rows.forEach(this::addEntry);
    }

    void resize(int x, int y, int width, int height) {
        double scroll = scrollAmount();
        for (Row row : children()) row.setHeight(row.height(width - (page instanceof WorldEditPage ? 28 : 36)));
        updateSizeAndPosition(width, height, x, y);
        setScrollAmount(scroll);
        children().forEach(Row::position);
    }

    void refresh() {
        if (children().stream().anyMatch(row -> row.getHeight() != row.height(getWidth() - (page instanceof WorldEditPage ? 28 : 36))))
            resize(getX(), getY(), getWidth(), getHeight());
    }

    int naturalHeight(int width) {
        int contentWidth = width - (page instanceof WorldEditPage ? 28 : 36);
        return children().stream().mapToInt(row -> row.height(contentWidth)).sum() + 4
            + (page instanceof WorldEditPage ? page.p(28) : 0);
    }

    boolean focusEditor(Screen screen, EditBox editor) {
        for (Row row : children()) if (row.widget == editor) {
            screen.setFocused(this);
            setFocused(row);
            row.setFocused(row.control);
            return true;
        }
        return false;
    }

    @Override public int getRowWidth() { return getWidth() - (page instanceof WorldEditPage ? 20 : 28); }
    @Override protected int contentHeight() {
        return super.contentHeight() + (page instanceof WorldEditPage ? page.p(28) : 0);
    }
    @Override protected void extractListBackground(GuiGraphicsExtractor graphics) {}
    @Override protected void extractListSeparators(GuiGraphicsExtractor graphics) {}
    @Override public int scrollbarWidth() { return 3; }

    final class Row extends ContainerObjectSelectionList.Entry<Row> {
        private final AbstractWidget widget;
        private final AbstractWidget control;
        private final Component caption;
        private Button accessory;
        private Component inlineDescription;
        Row(AbstractWidget widget, Component caption) {
            this.widget = widget;
            this.caption = caption;
            control = widget instanceof EditBox editor ? new NativeTextBox(page, editor) : widget;
        }
        private boolean route() { return more || page instanceof WorldEditPage && widget instanceof Button; }
        private int controlWidth(int width) { return page.compact() ? Math.min(110, width / 2) : 140; }
        private boolean stacked(int width) { return page instanceof WorldSubpage && width < 260; }
        private Component description() { return inlineDescription == null ? SettingsText.description(widget) : inlineDescription; }
        int height(int width) {
            if (widget instanceof EditBox) return page.density().tight() ? 40 : page instanceof WorldEditPage ? 56 : 64;
            if (route()) return page.density().tight() ? 22 : page instanceof WorldEditPage ? 32 : 48;
            int available = stacked(width) ? width : Math.max(30, width - controlWidth(width) - 12);
            int lines = Minecraft.getInstance().font.split(MenuText.secondary(description()),
                Math.max(1, Math.round(available * Minecraft.getInstance().font.lineHeight / (float) page.density().detail()))).size();
            int textHeight = page.density().body() + (description().getString().isBlank() ? 0
                : page.density().tight() ? 4 + lines * 7 : 12 + Math.max(0, lines - 1) * 10);
            return (page.density().tight() ? 12 : 20)
                + (stacked(width) ? textHeight + page.density().control() + 10 : Math.max(page.density().control(), textHeight));
        }
        void position() {
            int x = getContentX() + 2, y = getContentY() + (page instanceof WorldEditPage ? page.p(4) : 0),
                width = getContentWidth() - 4;
            if (widget instanceof EditBox editor) {
                control.setRectangle(width, page.density().control(), x, y + (page.density().tight() ? 18 : 24));
            } else if (route()) {
                control.setRectangle(width, page.density().tight() ? 16 : page instanceof WorldEditPage ? 26 : 40, x, y + (page.density().tight() ? 2 : 4));
            } else {
                int control = widget instanceof CycleButton<?> cycle && cycle.getValue() instanceof Boolean ? 54 : controlWidth(width);
                boolean customize = accessory != null && accessory.active;
                if (accessory != null) accessory.visible = customize;
                int controlY = stacked(width) ? y + getHeight() - page.density().control() - 10 : y + (getHeight() - page.density().control()) / 2;
                widget.setRectangle(customize ? control - 50 : control, page.density().control(), x + width - control, controlY);
                if (customize) accessory.setRectangle(44, page.density().control(), x + width - 44, controlY);
            }
        }
        @Override public List<? extends GuiEventListener> children() { return accessory == null ? List.of(control) : List.of(control, accessory); }
        @Override public List<? extends NarratableEntry> narratables() { return accessory == null ? List.of(control) : List.of(control, accessory); }
        @Override public void extractContent(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float partialTick) {
            position();
            int x = getContentX() + 2, y = getContentY() + (page instanceof WorldEditPage ? page.p(4) : 0),
                width = getContentWidth() - 4;
            if (!route()) {
                if (widget instanceof EditBox) {
                    page.text(graphics, SettingsText.label(caption), x, y + (page.density().tight() ? 6 : 8),
                        page.density().body(), page.theme().ink(), false);
                } else {
                    int available = stacked(width) ? width : Math.max(30, width - controlWidth(width) - 12);
                    var font = Minecraft.getInstance().font;
                    var detail = description();
                    List<FormattedCharSequence> lines = detail.getString().isBlank() ? List.of()
                        : font.split(MenuText.secondary(detail), Math.max(1, Math.round(available * font.lineHeight / (float) page.density().detail())));
                    int detailStep = page.density().tight() ? 7 : 10;
                    int textHeight = page.density().body() + (lines.isEmpty() ? 0 : page.density().tight() ? 4 + lines.size() * detailStep : 12 + (lines.size() - 1) * 10);
                    float textY = stacked(width) ? y + 10 : widget.getY() + widget.getHeight() / 2f - textHeight / 2f;
                    Component label = page.fitLabel(MenuText.styled(SettingsText.label(caption)),
                        available * font.lineHeight / (float) page.density().body(), hovered || isFocused() && page.focusVisible());
                    page.text(graphics, label, x, textY, page.density().body(), page.theme().ink(), false);
                    for (int i = 0; i < lines.size(); i++) {
                        graphics.pose().pushMatrix();
                        graphics.pose().translate(x, textY + (page.density().tight() ? 10 : 14) + i * detailStep);
                        graphics.pose().scale(page.density().detail() / (float) font.lineHeight);
                        graphics.text(font, lines.get(i), 0, 0, page.theme().muted(), false);
                        graphics.pose().popMatrix();
                    }
                    if (this != WorldFormList.this.children().getLast())
                        graphics.fill(x, y + getHeight() - 1, x + width, y + getHeight(), page.theme().edge());
                }
            }
            control.extractRenderState(graphics, mouseX, mouseY, partialTick);
            if (accessory != null) accessory.extractRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }
}
