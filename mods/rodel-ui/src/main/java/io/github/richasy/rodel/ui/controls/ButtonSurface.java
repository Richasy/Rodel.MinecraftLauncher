package io.github.richasy.rodel.ui.controls;

import io.github.richasy.rodel.ui.MenuTheme;
import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Shared shallow relief, in design units. The caller owns bounds, content and interaction. */
public final class ButtonSurface {
    public enum Role { STANDARD, ACCENT }
    public enum State { REST, HOVER, PRESSED, DISABLED }

    private ButtonSurface() {}

    public static int draw(GuiGraphicsExtractor graphics, MenuTheme theme, int width, int height,
                           Role role, State state) {
        if (width <= 0 || height <= 0) return theme.muted();
        boolean enabled = state != State.DISABLED;
        boolean accent = enabled && role == Role.ACCENT;
        boolean down = enabled && state == State.PRESSED;
        boolean hover = enabled && state == State.HOVER;
        int ink = !enabled ? theme.muted() : accent ? theme.primaryInk() : down ? theme.pressedInk() : theme.ink();
        if (theme.highContrast()) {
            graphics.fill(0, 0, width, height, accent ? theme.primary() : theme.surface());
            graphics.outline(0, 0, width, height, hover || down ? theme.focus() : theme.edge());
            return ink;
        }
        int face = !enabled ? theme.light() ? 0xFFE0DED7 : 0xFF333D40
            : accent ? hover ? theme.primaryHover() : theme.primary()
            : down ? theme.pressed() : hover ? theme.hover() : theme.surface();
        if (accent && down) face = blend(face, 0xFF14230F, .24f);
        int border = accent ? blend(face, 0xFF172812, .37f)
            : theme.light() ? 0xFFB8AD94 : 0xFF52605E;
        int top = accent ? blend(face, 0xFFF5FFDB, .30f)
            : theme.light() ? 0xFFFFFEF7 : 0xFF63716E;
        int bottom = accent ? blend(face, 0xFF172812, .27f)
            : theme.light() ? 0xFFC7BDA6 : 0xFF121C20;
        graphics.fill(0, 0, width, height, face);
        graphics.outline(0, 0, width, height, border);
        if (width > 4 && height > 4) {
            if (!down && enabled) graphics.fillGradient(1, 1, width - 1, height - 2,
                theme.light() ? 0x16FFFFFF : 0x09FFFFFF, 0);
            // A single raised face: thin light top/left and a restrained lower edge.
            graphics.fill(1, 1, width - 1, down ? 3 : 2, down ? bottom : top);
            graphics.fill(1, 2, 2, height - 1, down ? bottom : blend(top, face, .4f));
            graphics.fill(1, height - (down ? 2 : 3), width - 1, height - 1, down ? top : bottom);
            graphics.fill(width - 2, 2, width - 1, height - 2, down ? top : blend(bottom, face, .45f));
            if (width >= 12 && height >= 12) {
                int glint = blend(face, top, enabled && !down ? .85f : .22f);
                graphics.fill(3, 3, 7, 4, glint);
                graphics.fill(3, 3, 4, 7, glint);
            }
        }
        return ink;
    }

    private static int blend(int from, int to, float amount) {
        int r = Math.round((from >> 16 & 255) * (1 - amount) + (to >> 16 & 255) * amount);
        int g = Math.round((from >> 8 & 255) * (1 - amount) + (to >> 8 & 255) * amount);
        int b = Math.round((from & 255) * (1 - amount) + (to & 255) * amount);
        return 0xFF000000 | r << 16 | g << 8 | b;
    }
}
