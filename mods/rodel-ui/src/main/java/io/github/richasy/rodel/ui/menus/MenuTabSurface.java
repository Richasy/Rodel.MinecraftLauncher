package io.github.richasy.rodel.ui.menus;

import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;

/** Persistent tab selection, separate from a button's transient pressed state. */
public final class MenuTabSurface {
    private MenuTabSurface() {}

    public static void draw(NativeMenuPage page, GuiGraphicsExtractor graphics, AbstractWidget tab,
                            Component message, boolean selected) {
        var theme = page.theme();
        boolean hovered = tab.isHovered() && tab.active;
        graphics.fill(tab.getX(), tab.getY(), tab.getRight(), tab.getBottom(),
            !tab.active ? theme.surface() : selected ? theme.selection() : hovered ? theme.hover() : theme.surface());
        graphics.outline(tab.getX(), tab.getY(), tab.getWidth(), tab.getHeight(), theme.edge());
        if (selected) graphics.fill(tab.getX() + 1, tab.getBottom() - 2, tab.getRight() - 1, tab.getBottom() - 1, theme.focus());
        if (tab.isFocused() && page.focusVisible())
            graphics.outline(tab.getX(), tab.getY(), tab.getWidth(), tab.getHeight(), theme.focus());
        var font = Minecraft.getInstance().font;
        int size = page.density().body();
        var label = page.fitLabel(MenuText.styled(message),
            Math.max(0, tab.getWidth() - page.density().inset() * 2) * font.lineHeight / (float) size,
            hovered || tab.isFocused() && page.focusVisible());
        page.beginNativeText();
        try {
            MenuText.extract(graphics, font, label,
                tab.getX() + (tab.getWidth() - font.width(label) * size / (float) font.lineHeight) / 2,
                tab.getY() + (tab.getHeight() - size) / 2f, size, tab.active ? theme.ink() : theme.muted());
        } finally { page.endNativeText(); }
    }
}
