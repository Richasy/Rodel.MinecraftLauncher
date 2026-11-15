package io.github.richasy.rodel.ui.workstation;

import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Shared integer geometry for the native recipe widgets and their integrated side page. */
public final class WorkstationRecipeLayout {
    /** Recipe page width, including its category rail. */
    public static final int WIDTH = 148;
    /** Gap between the two independently interactive pages. */
    public static final int GAP = 6;
    /** First recipe cell, beyond the integrated category rail. */
    public static final int GRID_X = 38;
    /** Aligns recipes with the outer edge of the workstation's first input row. */
    public static final int GRID_Y = 32;
    /** Native 25-unit recipe cells separated by one unit. */
    public static final int PITCH = 26;
    /** Four columns preserve all twenty native recipes in five rows. */
    public static final int COLUMNS = 4;
    /** Neutral parchment text shared by input, hint and pagination. */
    public static final int INK = 0xFF51493D;
    /** Quiet secondary text on parchment. */
    public static final int MUTED = 0xFF786E5E;

    private WorkstationRecipeLayout() {}

    /** Centers either the recipe page alone or the complete two-page group. */
    public static int left(int viewportWidth, boolean narrow) {
        return (viewportWidth - WIDTH - (narrow ? 0 : GAP + WorkstationSurface.WIDTH)) / 2;
    }

    /** Paints one page with a search field and an integrated category rail. */
    public static void background(GuiGraphicsExtractor g, int x, int y, boolean searchFocused) {
        int h = WorkstationSurface.HEIGHT;
        rect(g, x, y, WIDTH, h, 0xFF817665);
        rect(g, x + 1, y + 1, WIDTH - 2, h - 2, 0xFFC9C1B1);
        rect(g, x + 1, y + 1, WIDTH - 2, 1, 0xFFE7E0D3);
        rect(g, x + 1, y + 23, WIDTH - 2, 1, 0xFFA89D89);
        rect(g, x + 6, y + 4, 113, 16, 0xFFE0D9CB);
        rect(g, x + 6, y + 4, 113, 1, 0xFF9B8F7A);
        rect(g, x + 6, y + 19, 113, 1, 0xFFEFE8DB);
        if (searchFocused) rect(g, x + 6, y + 19, 113, 1, 0xFF8E7049);
        g.outline(x + 11, y + 7, 5, 5, MUTED);
        rect(g, x + 15, y + 11, 2, 2, MUTED);
        rect(g, x + 17, y + 13, 1, 1, MUTED);
        rect(g, x + 33, y + GRID_Y, 1, 129, 0xFFB5AA96);
    }

    /** Draws a native filter action using its actual Boolean selection. */
    public static void filter(GuiGraphicsExtractor g, int x, int y, boolean selected, boolean hovered, boolean focused) {
        rect(g, x, y, 20, 16, selected ? 0xFFAC9B7D : hovered ? 0xFFE0D5C2 : 0xFFC9C1B1);
        if (focused || selected) g.outline(x, y, 20, 16, 0xFF8E7A59);
        int ink = selected ? 0xFF493D29 : INK;
        rect(g, x + 5, y + 4, 10, 2, ink);
        rect(g, x + 6, y + 6, 8, 1, ink);
        rect(g, x + 7, y + 7, 6, 1, ink);
        rect(g, x + 8, y + 8, 4, 1, ink);
        rect(g, x + 9, y + 9, 2, 4, ink);
    }

    /** Draws the native pagination action inside its hit target. */
    public static void pageArrow(GuiGraphicsExtractor g, int x, int y, int w, int h,
                                 boolean forward, boolean hovered, boolean focused) {
        if (hovered) rect(g, x, y, w, h, 0xFFE1D6C3);
        if (focused) g.outline(x, y, w, h, 0xFF8E7A59);
        for (int row = 0; row < 9; row++) {
            int length = 5 - Math.abs(4 - row);
            rect(g, x + (w - 5) / 2 + (forward ? 0 : 5 - length), y + (h - 9) / 2 + row, length, 1, INK);
        }
    }

    private static void rect(GuiGraphicsExtractor g, int x, int y, int w, int h, int color) {
        g.fill(x, y, x + w, y + h, color);
    }
}
