package io.github.richasy.rodel.ui.menus;

import java.util.List;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.settings.SettingsText;
import io.github.richasy.rodel.ui.platform.NativeWidgets;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

public final class StatsMenuPage extends NativeMenuPage {
    private AbstractSelectionList<?> activeList;
    private int panelLeft;
    private int panelTop;
    private int panelWidth;
    private int panelHeight;
    private boolean general;

    public StatsMenuPage(Screen screen, MenuContext appearance) { super(screen, appearance); }

    @Override public void initialized() {
        activeList = null;
        for (var widget : NativeWidgets.get(screen())) {
            if (widget instanceof AbstractSelectionList<?> list) activeList = list;
            if (widget instanceof Button button && (SettingsText.hasKey(button.getMessage(), "gui.done")
                || SettingsText.hasKey(button.getMessage(), "gui.back"))) {
                button.setMessage(Component.translatable("gui.back"));
                button.setRectangle(density().control(), density().control(), 24, 16);
                ((ThemedButton) button).rodel$setStyle(this, MenuIcon.BACK, false, true, false);
            }
        }
        var tabs = ((StatsMenuAccess) screen()).rodel$statsTabs();
        if (tabs != null) {
            int width = Math.min(270, screen().width - 168);
            tabs.setRectangle(width, density().control(), screen().width - 24 - width, 16);
            var children = tabs.children();
            for (int i = 0; i < children.size(); i++) {
                if (children.get(i) instanceof AbstractWidget button) {
                    int x = tabs.getX() + width * i / children.size();
                    int right = tabs.getX() + width * (i + 1) / children.size();
                    button.setRectangle(right - x, density().control(), x, tabs.getY());
                }
            }
        }
        if (activeList == null) return;
        general = activeList.getClass().getName().endsWith("$GeneralStatisticsList");
        boolean items = activeList.getClass().getName().endsWith("$ItemStatisticsList");
        int listWidth = Math.min(items ? 400 : 520, screen().width - 72);
        panelLeft = (screen().width - listWidth) / 2 - 12;
        panelTop = density().tight() ? 46 : 58;
        panelWidth = listWidth + 24;
        panelHeight = screen().height - panelTop - 20;
        double scroll = activeList.scrollAmount();
        if (general) for (var entry : activeList.children()) entry.setHeight(density().tight() ? 12 : 18);
        int listTop = panelTop + (general ? density().inset() * 2 + density().detail() + density().gap() : 12);
        activeList.updateSizeAndPosition(listWidth, Math.max(24, panelTop + panelHeight - 12 - listTop),
            panelLeft + 12, listTop);
        activeList.setScrollAmount(scroll);
    }

    @Override protected void panels(GuiGraphicsExtractor graphics) {
        if (activeList != null) theme().panel(graphics, panelLeft, panelTop, panelWidth, panelHeight);
        beginNativeText();
        try {
            var font = Minecraft.getInstance().font;
            MenuText.extract(graphics, font, MenuText.styled(screen().getTitle()), 58, 21,
                density().tight() ? density().heading() : 12, theme().ink());
            if (general && activeList != null) {
                int x = activeList.getX() + 14;
                int size = density().detail();
                MenuText.extract(graphics, font, MenuText.secondary(Component.translatable("rodel.stats.column.name")),
                    x, panelTop + density().inset() * 2, size, theme().muted());
                var value = MenuText.secondary(Component.translatable("rodel.stats.column.value"));
                MenuText.extract(graphics, font, value,
                    activeList.getRight() - 14 - font.width(value) * size / (float) font.lineHeight,
                    panelTop + density().inset() * 2, size, theme().muted());
            }
        } finally { endNativeText(); }
    }

    @Override protected void style(net.minecraft.client.gui.components.events.GuiEventListener listener) {
        // The dedicated header owns its icon-only back style; the native body keeps live controls.
    }

    public void content(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick) {
        for (var widget : List.copyOf(NativeWidgets.get(screen()))) {
            if (widget instanceof StringWidget && widget.getMessage().equals(screen().getTitle())) continue;
            widget.extractRenderState(graphics, mouseX, mouseY, partialTick);
        }
    }
}
