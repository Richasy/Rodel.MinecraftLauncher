package io.github.richasy.bettertools.client;

import io.github.richasy.bettertools.BetterTools;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;

/** Cube-only tooltip appearance with a locally saved palette and the native Minecraft font. */
public final class CubeTooltipLook {
    /** Identifies the cube's presentation without modifying the stored item's tooltip components. */
    public static final Identifier STYLE = BetterTools.id("cube_tooltip");
    private static final CubeTooltipLook CURRENT = new CubeTooltipLook();
    private int ink, muted, surface, edge, top, bottom, slot, selection, focus, keycap;

    private CubeTooltipLook() { }

    /** Reads the cube's own palette without borrowing the active screen or another mod. */
    public static CubeTooltipLook current() { CURRENT.refresh(); return CURRENT; }

    private void refresh() {
        boolean dark = CubeClientConfig.dark();
        ink = dark ? 0xFFE8E9E5 : 0xFF3B3020; muted = dark ? 0xFFC2C4C3 : 0xFF76674C;
        surface = dark ? 0xF0202427 : 0xF8F5EFDC; edge = dark ? 0xFF5C6062 : 0xFFB69A67;
        top = dark ? 0xFF454A4D : 0xFFFFFFF5; bottom = dark ? 0xFF101416 : 0xFFDBCFB3;
        slot = dark ? 0xFF24282B : 0xFFF8F3E6; selection = dark ? 0xFF343431 : 0xFFFFF9E7;
        focus = 0xFFE9B743; keycap = dark ? 0xFF55595B : 0xFFE6DCC5;
    }

    /** Supplies the theme's primary text color to native tooltip extraction. */
    public int ink() { return ink; }

    /** Supplies the theme's secondary text and outline color. */
    public int muted() { return muted; }

    /** Supplies the same amber selection marker in both approved palettes. */
    public int accent() { return focus; }

    /** Applies readable neutral colors with the native font, retaining item-specific semantic colors. */
    public Component text(Component message) { return text(message, ink); }

    /** Adapts one text role without changing the stored item or its component styles. */
    public Component text(Component message, int neutral) {
        var result = Component.empty();
        message.visit((style, value) -> {
            var color = style.getColor();
            boolean replace = color == null || switch (color.getValue()) {
                case 0xFFFFFF, 0xE0E0E0, 0xAAAAAA, 0xA0A0A0, 0x808080, 0x555555 -> true;
                default -> false;
            };
            var presentation = style.withShadowColor(0);
            result.append(Component.literal(value).setStyle(replace ? presentation.withColor(neutral) : presentation));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    /** Draws only the cube tooltip's background using the cube's own panel palette. */
    public void panel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        rounded(graphics, x + 1, y + 2, width, height, 0x4408090A);
        rounded(graphics, x, y, width, height, edge);
        rounded(graphics, x + 1, y + 1, width - 2, height - 2, surface);
        graphics.fill(x + 3, y + 1, x + width - 3, y + 2, top);
        graphics.fill(x + 3, y + height - 2, x + width - 3, y + height - 1, bottom);
    }

    /** Draws the approved inset cell with an amber double frame and unchanged native item geometry. */
    public void slot(GuiGraphicsExtractor graphics, int x, int y, boolean picked) {
        if (picked) rounded(graphics, x - 1, y - 1, 26, 26, 0x665B3A06);
        rounded(graphics, x, y, 24, 24, picked ? focus : edge);
        rounded(graphics, x + 1, y + 1, 22, 22, picked ? selection : slot);
        graphics.fill(x + 3, y + 1, x + 21, y + 2, picked ? 0xFFFFEBA2 : top);
        graphics.fill(x + 3, y + 22, x + 21, y + 23, picked ? 0xFFC28B23 : bottom);
        if (picked) graphics.outline(x + 2, y + 2, 20, 20, focus);
    }

    /** Draws one subtle separator without turning it into a second panel border. */
    public void rule(GuiGraphicsExtractor graphics, int x, int y, int endX) {
        if (endX > x) graphics.fill(x, y, endX, y + 1, (edge & 0xFFFFFF) | 0x99000000);
    }

    /** Draws the compact Tab keycap that shares the card's current light/dark palette. */
    public void keycap(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        rounded(graphics, x, y, width, height, keycap);
    }

    /** Draws the selected original's independent dark tooltip surface in either card theme. */
    public void detailPanel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        rounded(graphics, x - 1, y - 1, width + 2, height + 2, 0xFF09060C);
        rounded(graphics, x, y, width, height, 0xFF421A62);
        rounded(graphics, x + 1, y + 1, width - 2, height - 2, 0xF012091A);
    }

    private static void rounded(GuiGraphicsExtractor graphics, int x, int y, int width, int height, int color) {
        graphics.fill(x + 2, y, x + width - 2, y + 1, color);
        graphics.fill(x + 1, y + 1, x + width - 1, y + 2, color);
        graphics.fill(x, y + 2, x + width, y + height - 2, color);
        graphics.fill(x + 1, y + height - 2, x + width - 1, y + height - 1, color);
        graphics.fill(x + 2, y + height - 1, x + width - 2, y + height, color);
    }
}
