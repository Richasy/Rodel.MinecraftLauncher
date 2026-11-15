package io.github.richasy.rodel.ui;

import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.settings.SettingsText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;

public final class SidebarNavigation {
    private SidebarNavigation() {}

    public static void render(GuiGraphicsExtractor graphics, NativeFormContext page, AbstractWidget widget,
                              boolean selected, boolean focused) {
        MenuTheme theme = page.theme();
        int ink = widget.active ? selected ? theme.light() ? 0xFF463116 : theme.ink() : theme.muted() : theme.edge();
        if (selected || widget.active && (widget.isHovered() || focused)) {
            int face = theme.highContrast() ? theme.surface()
                : selected ? theme.light() ? 0xFFE7C991 : 0xFF3C4A4D : theme.surface();
            graphics.pose().pushMatrix();
            graphics.pose().translate(widget.getX(), widget.getY());
            graphics.pose().scale(page.scale());
            int width = Math.round(widget.getWidth() / page.scale());
            int height = Math.round(widget.getHeight() / page.scale());
            graphics.fill(0, 0, width, height, face);
            if (selected) {
                graphics.outline(0, 0, width, height, theme.edge());
                if (!theme.highContrast()) {
                    int dark = theme.light() ? 0xA0A78042 : 0xFF233134;
                    int light = theme.light() ? 0xFFFFF5D8 : 0xA0859A97;
                    graphics.fill(1, 1, width - 1, 5, dark);
                    graphics.fill(1, 1, 5, height - 1, dark);
                    graphics.fill(2, height - 3, width - 1, height - 1, light);
                    graphics.fill(width - 3, 2, width - 1, height - 1, light);
                    graphics.fillGradient(5, 5, width - 3, height / 2, theme.light() ? 0x20A78042 : 0x30233134, 0);
                }
            }
            graphics.pose().popMatrix();
        }
        if (focused) graphics.outline(widget.getX(), widget.getY(), widget.getWidth(), widget.getHeight(), theme.focus());
        var font = Minecraft.getInstance().font;
        int size = page.density().tight() ? page.density().body() : page.p(page.compact() ? 15 : 18);
        Component label = page.fitLabel(MenuText.styled(SettingsText.label(widget.getMessage())),
            (widget.getWidth() - page.p(28)) * font.lineHeight / (float) size, widget.isHovered() || focused);
        MenuText.extract(graphics, font, label, widget.getX() + page.p(14),
            widget.getY() + (widget.getHeight() - size) / 2f, size, ink);
    }
}
