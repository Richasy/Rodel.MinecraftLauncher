package io.github.richasy.bettertools.client;

import net.minecraft.client.gui.GuiGraphicsExtractor;

/** Native GUI pixel geometry for the approved wire cube, information and mouse symbols. */
public final class CubeTooltipIcons {
    private CubeTooltipIcons() { }

    /** Draws the header's isometric outlined cube without changing the actual inventory item icon. */
    public static void cube(GuiGraphicsExtractor graphics, int x, int y, int ink) {
        line(graphics, x + 5, y, x, y + 3, ink);
        line(graphics, x + 5, y, x + 10, y + 3, ink);
        line(graphics, x, y + 3, x + 5, y + 6, ink);
        line(graphics, x + 10, y + 3, x + 5, y + 6, ink);
        line(graphics, x, y + 3, x, y + 8, ink);
        line(graphics, x + 10, y + 3, x + 10, y + 8, ink);
        line(graphics, x + 5, y + 6, x + 5, y + 11, ink);
        line(graphics, x, y + 8, x + 5, y + 11, ink);
        line(graphics, x + 10, y + 8, x + 5, y + 11, ink);
        line(graphics, x + 3, y + 2, x + 7, y + 4, ink);
    }

    /** Draws the small circled information symbol in the card header. */
    public static void info(GuiGraphicsExtractor graphics, int x, int y, int ink) {
        pixels(graphics, x, y, ink, new String[]{"...####...", ".##....##.", ".#..##..#.", "#........#", "#...##...#", "#...##...#", "#...##...#", ".#..##..#.", ".##....##.", "...####..."});
    }

    /** Draws the footer mouse, including the highlighted scroll wheel and native button split. */
    public static void mouse(GuiGraphicsExtractor graphics, int x, int y, int ink, int accent) {
        pixels(graphics, x, y, ink, new String[]{"...###...", ".##...##.", ".#..#..#.", "#...#...#", "#...#...#", "#...#...#", "#########", "#.......#", "#.......#", ".#.....#.", ".##...##.", "...###..."});
        graphics.fill(x + 4, y + 2, x + 5, y + 5, accent);
    }

    private static void pixels(GuiGraphicsExtractor graphics, int x, int y, int ink, String[] rows) {
        for (int row = 0; row < rows.length; row++) for (int col = 0; col < rows[row].length(); col++)
            if (rows[row].charAt(col) == '#') graphics.fill(x + col, y + row, x + col + 1, y + row + 1, ink);
    }

    private static void line(GuiGraphicsExtractor graphics, int x, int y, int endX, int endY, int ink) {
        int steps = Math.max(Math.abs(endX - x), Math.abs(endY - y));
        for (int i = 0; i <= steps; i++) {
            int px = x + Math.round((endX - x) * i / (float)steps), py = y + Math.round((endY - y) * i / (float)steps);
            graphics.fill(px, py, px + 1, py + 1, ink);
        }
    }
}
