package io.github.richasy.rodel.ui;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import io.github.richasy.rodel.ui.controls.ButtonSurface;

public record MenuTheme(
    int ink, int muted, int surface, int edge, int bevelTop, int bevelLeft,
    int bevelBottom, int bevelRight, int shadow, int hover, int pressed, int pressedInk,
    int primary, int primaryHover, int primaryInk, int focus, boolean light, boolean highContrast
) {
    public static final MenuTheme LIGHT = new MenuTheme(
        0xFF30291E, 0xFF574A34, 0xEEFFFCEF, 0xA09B722F,
        0xF5FFFFF9, 0xCCFFFFF9, 0x4D8C6A34, 0x338C6A34, 0x33694C1F,
        0xFFFFF4D9, 0xFFC7A366, 0xFF34240F, 0xFF5B8730, 0xFF699739, 0xFFFFFFF0,
        0xFF8B581D, true, false
    );
    public static final MenuTheme DARK = new MenuTheme(
        0xFFF0F1E6, 0xFFC0CCC3, 0xE8182225, 0xA06E827F,
        0x3BE2E6CF, 0x1FE2E6CF, 0xB8020C0F, 0x66020C0F, 0x6B040C0F,
        0xFF344346, 0xFF283639, 0xFFE0E7D6, 0xFF76AC47, 0xFF86BC55, 0xFFFFFFE9,
        0xFFF4DE9B, false, false
    );
    public static final MenuTheme HIGH_CONTRAST = new MenuTheme(
        0xFFFFFFFF, 0xFFFFFFFF, 0xFF000000, 0xFFFFFFFF,
        0, 0, 0, 0, 0, 0xFF000000, 0xFF000000, 0xFFFFFFFF,
        0xFFFFFF00, 0xFFFFFF00, 0xFF000000, 0xFFFFFF00, false, true
    );

    public int selection() {
        return light && !highContrast ? 0xFFE7C991 : pressed;
    }

    public void panel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        if (!highContrast) {
            graphics.fill(x + 1, y + 3, x + width + 1, y + height + 3, 0x22040C0F);
        }
        graphics.fill(x, y, x + width, y + height, surface);
        graphics.outline(x, y, width, height, edge);
        if (!highContrast) {
            edges(graphics, x, y, width, height, 1, bevelTop, bevelLeft, bevelBottom, bevelRight, 1);
        }
    }

    public int button(GuiGraphicsExtractor graphics, int width, int height,
                      boolean accent, boolean highlighted, boolean down, boolean enabled) {
        return ButtonSurface.draw(graphics, this, width, height,
            accent ? ButtonSurface.Role.ACCENT
                : ButtonSurface.Role.STANDARD,
            !enabled ? ButtonSurface.State.DISABLED
                : down ? ButtonSurface.State.PRESSED
                : highlighted ? ButtonSurface.State.HOVER
                : ButtonSurface.State.REST);
    }

    public int button(GuiGraphicsExtractor graphics, int width, int height,
                      boolean accent, boolean highlighted, boolean down) {
        return button(graphics, width, height, accent, highlighted, down, true);
    }
    public void switchTrack(GuiGraphicsExtractor graphics, int y, boolean on,
                            boolean highlighted, boolean down, boolean enabled) {
        int track = on ? highlighted ? primaryHover : primary : highlighted ? hover : surface;
        int border = highContrast ? edge : on ? primary : muted;
        int thumb = on ? primaryInk : ink;
        if (!enabled) {
            track = surface;
            border = muted;
            thumb = muted;
        }
        graphics.fill(0, y, 52, y + 28, down ? pressed : track);
        graphics.outline(0, y, 52, 28, border);
        int x = on ? 28 : 4;
        graphics.fill(x, y + 4, x + 20, y + 24, thumb);
    }

    private static void edges(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                              int depth, int top, int left, int bottom, int right, int border) {
        graphics.fill(x + border, y + border, x + width - border, y + border + depth, top);
        graphics.fill(x + border, y + border, x + border + depth, y + height - border, left);
        graphics.fill(x + border, y + height - border - (depth > 2 ? 1 : depth),
            x + width - border, y + height - border, bottom);
        graphics.fill(x + width - border - (depth > 2 ? 2 : depth), y + border,
            x + width - border, y + height - border, right);
    }
}
