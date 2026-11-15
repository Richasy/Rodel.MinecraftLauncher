package io.github.richasy.rodel.ui.menus;

import java.util.ArrayList;
import java.util.List;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.SidebarNavigation;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class AdvancementsMenuPage extends NativeMenuPage {
    private final List<AbstractWidget> navigation = new ArrayList<>();
    private int panelX;
    private int panelWidth;
    private int panelHeight;
    private boolean arranging;

    public AdvancementsMenuPage(Screen screen, MenuContext appearance) { super(screen, appearance); }
    public int treeX() { return panelX + 8; }
    public int treeY() { return 64; }
    public int treeWidth() { return Math.max(32, panelWidth - 16); }
    public int treeHeight() { return Math.max(32, panelHeight - 16); }
    public boolean containsTree(double x, double y) {
        return x >= treeX() && x < treeX() + treeWidth() && y >= treeY() && y < treeY() + treeHeight();
    }

    @Override
    public void initialized() {
        if (arranging) return;
        arranging = true;
        try {
            var widgets = NativeWidgets.get(screen());
            widgets.removeAll(navigation);
            navigation.clear();
            for (AbstractWidget widget : widgets) {
                if (widget instanceof StringWidget && widget.getMessage().equals(screen().getTitle())) widget.visible = false;
                if (widget instanceof Button button && (SettingsText.hasKey(button.getMessage(), "gui.done")
                    || SettingsText.hasKey(button.getMessage(), "gui.back"))) {
                    button.setMessage(Component.translatable("gui.back"));
                    button.setTooltip(Tooltip.create(button.getMessage()));
                    button.setRectangle(density().control(), density().control(), 24, 16);
                    ((ThemedButton) button).rodel$setStyle(this, MenuIcon.BACK, false, true, false);
                }
            }
            var access = (AdvancementsMenuAccess) screen();
            int sidebar = access.rodel$tabs().isEmpty() ? 0 : compact() ? 92 : 124;
            panelX = 24 + sidebar;
            panelWidth = screen().width - panelX - 24;
            panelHeight = Math.max(48, screen().height - 76);
            access.rodel$origin(treeX() - 9, treeY() - 18);
            if (sidebar == 0) return;
            LinearLayout column = LinearLayout.vertical().spacing(4);
            for (var tab : access.rodel$tabs().values()) {
                column.addChild(new Button(0, 0, sidebar - 20, density().tight() ? 18 : 28, tab.getTitle(),
                    ignored -> access.rodel$advancements().setSelectedTab(tab.getRootAdvancement(), true),
                    narration -> narration.get()) {
                    @Override
                    protected void extractContents(GuiGraphicsExtractor graphics, int x, int y, float tick) {
                        SidebarNavigation.render(graphics, AdvancementsMenuPage.this, this,
                            access.rodel$selectedTab() == tab, isFocused() && focusVisible());
                    }
                });
            }
            column.arrangeElements();
            if (column.getHeight() <= panelHeight) {
                column.setPosition(24, 56);
                column.visitWidgets(navigation::add);
            } else {
                ScrollableLayout scroll = new ScrollableLayout(Minecraft.getInstance(), column, panelHeight);
                scroll.setMinHeight(0);
                scroll.setScrollbarSpacing(4);
                scroll.arrangeElements();
                scroll.setPosition(24, 56);
                scroll.visitWidgets(navigation::add);
            }
            widgets.addAll(navigation);
        } finally {
            arranging = false;
        }
    }

    @Override protected void style(GuiEventListener listener) {}

    @Override
    protected void panels(GuiGraphicsExtractor graphics) {
        theme().panel(graphics, panelX, 56, panelWidth, panelHeight);
        beginNativeText();
        try {
            MenuText.extract(graphics, Minecraft.getInstance().font, MenuText.styled(screen().getTitle()),
                58, 21, density().tight() ? density().heading() : 12, theme().ink());
        } finally {
            endNativeText();
        }
    }

    public void empty(GuiGraphicsExtractor graphics) {
        var font = Minecraft.getInstance().font;
        float center = treeX() + treeWidth() / 2f;
        int icon = density().tight() ? 18 : 28;
        int textSize = density().body();
        float textScale = textSize / (float) font.lineHeight;
        var lines = font.split(MenuText.secondary(((AdvancementsMenuAccess) screen()).rodel$emptyLabel()),
            Math.max(1, Math.round((treeWidth() - 32) * font.lineHeight / (float) textSize)));
        int textHeight = lines.size() * (textSize + 3) - (lines.isEmpty() ? 0 : 3);
        float top = treeY() + treeHeight() / 2f - (icon + 12 + textHeight) / 2f;
        MenuIcon.TROPHY.extract(graphics, center - icon / 2f, top, icon, theme().muted());
        beginNativeText();
        try {
            for (int i = 0; i < lines.size(); i++) {
                var line = lines.get(i);
                graphics.pose().pushMatrix();
                graphics.pose().translate(center - font.width(line) * textScale / 2f, top + icon + 12 + i * (textSize + 3));
                graphics.pose().scale(textScale);
                graphics.text(font, line, 0, 0, theme().muted(), false);
                graphics.pose().popMatrix();
            }
        } finally {
            endNativeText();
        }
    }

    public void tooltips(GuiGraphicsExtractor graphics) {
        var selected = ((AdvancementsMenuAccess) screen()).rodel$selectedTab();
        if (selected == null) return;
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(treeX(), treeY());
            graphics.nextStratum();
            selected.extractTooltips(graphics, treeX() - 9, treeY() - 18);
        } finally {
            graphics.pose().popMatrix();
        }
    }
}
