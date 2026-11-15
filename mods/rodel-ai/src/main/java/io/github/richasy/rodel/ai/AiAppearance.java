package io.github.richasy.rodel.ai;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Local AI panel colors and native GUI dimensions; no appearance is borrowed from another mod. */
final class AiAppearance {
    Theme theme() { return AiClientConfig.dark() ? Theme.DARK : Theme.LIGHT; }
    Density density() {
        var window = Minecraft.getInstance().getWindow();
        return window.getGuiScaledWidth() < 550 || window.getGuiScaledHeight() < 320 ? Density.COMPACT : Density.NORMAL;
    }
    boolean focusVisible() { return Minecraft.getInstance().getLastInputType().isKeyboard(); }

    enum Density {
        NORMAL(12, 24, 8, 6), COMPACT(10, 20, 6, 4);
        private final int heading, control, inset, gap;
        Density(int heading, int control, int inset, int gap) { this.heading = heading; this.control = control; this.inset = inset; this.gap = gap; }
        int heading() { return heading; }
        int body() { return 9; }
        int detail() { return 9; }
        int control() { return control; }
        int inset() { return inset; }
        int gap() { return gap; }
        int row() { return control + gap; }
    }

    enum Theme {
        LIGHT(0xFF30291E, 0xFF574A34, 0xF5FFFCEF, 0xFF9B987F, 0xFFE4E7DB),
        DARK(0xFFF0F1E6, 0xFFC0CCC3, 0xF5182225, 0xFF6E827F, 0xFF344346);
        private final int ink, muted, surface, edge, hover;
        Theme(int ink, int muted, int surface, int edge, int hover) { this.ink = ink; this.muted = muted; this.surface = surface; this.edge = edge; this.hover = hover; }
        int ink() { return ink; }
        int muted() { return muted; }
        int surface() { return surface; }
        int edge() { return edge; }
        int hover() { return hover; }
        int focus() { return this == DARK ? 0xFFD9E9D3 : 0xFF416C46; }
        void panel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
            graphics.fill(x, y, x + width, y + height, surface);
            graphics.outline(x, y, width, height, edge);
        }
    }
}
