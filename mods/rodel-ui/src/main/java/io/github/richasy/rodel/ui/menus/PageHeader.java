package io.github.richasy.rodel.ui.menus;

import io.github.richasy.rodel.ui.MenuDensity;
import io.github.richasy.rodel.ui.MenuText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;

/** Native GUI geometry for a back control and its associated title group. */
public record PageHeader(int left, int top, int width, int height, int controlY,
                         int textX, int titleY, int subtitleY) {
    public static PageHeader measure(MenuDensity density, int left, int top, int width, boolean subtitle) {
        return measure(density, left, top, width, subtitle, true);
    }

    public static PageHeader measure(MenuDensity density, int left, int top, int width, boolean subtitle, boolean navigation) {
        int innerGap = Math.max(1, density.gap() / 2);
        int textHeight = density.heading() + (subtitle ? innerGap + density.detail() : 0);
        int height = Math.max(density.control(), textHeight);
        int titleY = top + (height - textHeight) / 2;
        return new PageHeader(left, top, width, height, top + (height - density.control()) / 2,
            left + (navigation ? density.control() + density.gap() : 0), titleY, titleY + density.heading() + innerGap);
    }

    public void draw(NativeMenuPage page, GuiGraphicsExtractor graphics, Component title, Component subtitle) {
        var font = Minecraft.getInstance().font;
        int available = Math.max(0, left + width - textX);
        Component fittedTitle = page.fitLabel(MenuText.styled(title),
            available * font.lineHeight / (float) page.density().heading(), false);
        MenuText.extract(graphics, font, fittedTitle, textX, titleY, page.density().heading(), page.theme().ink());
        if (subtitle != null) {
            Component fitted = MenuText.secondary(page.fitLabel(MenuText.secondary(subtitle),
                available * font.lineHeight / (float) page.density().detail(), false));
            MenuText.extract(graphics, font, fitted, textX, subtitleY, page.density().detail(), page.theme().muted());
        }
    }
}
