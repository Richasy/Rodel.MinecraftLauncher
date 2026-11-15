package io.github.richasy.rodel.ui;

import net.minecraft.client.gui.GuiGraphicsExtractor;

public enum MenuIcon {
    PLAY, SERVER, WORLD, PEOPLE, SETTINGS, EXIT, ARROW, BACK, THEME, ACCESSIBILITY, PACK, FLASK,
    TROPHY, STATS, CHAT, FLAG, BUG, CHECK, CLOSE, PERSON_REMOVE, BLOCK, LINK, VIDEO, SHIELD, NEWS, LOCK, UNLOCK;

    public static MenuIcon nativeSprite(net.minecraft.resources.Identifier sprite) {
        if (!sprite.getNamespace().equals("minecraft")) return null;
        return switch (sprite.getPath()) {
            case "friends/accept", "pending_invite/accept", "icon/checkmark" -> CHECK;
            case "friends/reject", "pending_invite/reject" -> CLOSE;
            case "friends/remove", "player_list/remove_player" -> PERSON_REMOVE;
            case "friends/cancel" -> BLOCK;
            case "friends/friends", "friends/send_request", "icon/invite" -> PEOPLE;
            case "player_list/make_operator", "player_list/remove_operator" -> SHIELD;
            case "icon/link" -> LINK;
            case "icon/video_link" -> VIDEO;
            case "icon/news" -> NEWS;
            default -> null;
        };
    }

    public void extract(GuiGraphicsExtractor graphics, float x, float y, float size, int color) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(size / 24f);
        switch (this) {
            case LOCK, UNLOCK -> {
                box(graphics, 5, 11, 14, 10, color);
                if (this == LOCK) path(graphics, color, 8, 11, 8, 6, 10, 3, 14, 3, 16, 6, 16, 11);
                else path(graphics, color, 10, 11, 10, 6, 12, 3, 16, 3, 18, 6);
                line(graphics, 12, 15, 12, 18, color);
            }
            case NEWS -> {
                box(graphics, 3, 3, 18, 18, color);
                box(graphics, 6, 6, 5, 5, color);
                line(graphics, 14, 6, 18, 6, color);
                line(graphics, 14, 10, 18, 10, color);
                line(graphics, 6, 15, 18, 15, color);
                line(graphics, 6, 18, 18, 18, color);
            }
            case CHECK -> path(graphics, color, 4, 12, 10, 18, 21, 5);
            case CLOSE -> {
                line(graphics, 5, 5, 19, 19, color);
                line(graphics, 19, 5, 5, 19, color);
            }
            case PERSON_REMOVE -> {
                ellipse(graphics, 9, 7, 3, 3, color);
                path(graphics, color, 3, 21, 3, 17, 6, 13, 11, 13, 14, 17);
                line(graphics, 15, 12, 22, 12, color);
            }
            case BLOCK -> {
                ellipse(graphics, 12, 12, 9, 9, color);
                line(graphics, 6, 18, 18, 6, color);
            }
            case LINK -> {
                path(graphics, color, 10, 8, 14, 4, 18, 4, 21, 7, 21, 10, 16, 15);
                path(graphics, color, 14, 16, 10, 20, 6, 20, 3, 17, 3, 14, 8, 9);
                line(graphics, 8, 16, 16, 8, color);
            }
            case VIDEO -> {
                box(graphics, 3, 5, 18, 14, color);
                path(graphics, color, 10, 8, 16, 12, 10, 16, 10, 8);
            }
            case SHIELD -> path(graphics, color, 12, 2, 21, 6, 20, 14, 16, 19, 12, 22, 8, 19, 4, 14, 3, 6, 12, 2);
            case TROPHY -> {
                path(graphics, color, 7, 3, 17, 3, 17, 9, 15, 13, 12, 15, 9, 13, 7, 9, 7, 3);
                path(graphics, color, 7, 5, 3, 5, 3, 9, 6, 11);
                path(graphics, color, 17, 5, 21, 5, 21, 9, 18, 11);
                line(graphics, 12, 15, 12, 21, color);
                line(graphics, 7, 21, 17, 21, color);
            }
            case STATS -> {
                line(graphics, 3, 21, 21, 21, color);
                box(graphics, 5, 12, 3, 7, color);
                box(graphics, 11, 7, 3, 12, color);
                box(graphics, 17, 3, 3, 16, color);
            }
            case CHAT -> path(graphics, color, 3, 4, 21, 4, 21, 17, 10, 17, 5, 21, 5, 17, 3, 17, 3, 4);
            case FLAG -> {
                line(graphics, 5, 3, 5, 22, color);
                path(graphics, color, 5, 4, 19, 4, 16, 9, 19, 14, 5, 14);
            }
            case BUG -> {
                ellipse(graphics, 12, 13, 5, 7, color);
                line(graphics, 12, 7, 12, 20, color);
                for (int row : new int[] {8, 13, 18}) {
                    line(graphics, 3, row - 2, 7, row, color);
                    line(graphics, 17, row, 21, row - 2, color);
                }
                path(graphics, color, 8, 3, 10, 6, 14, 6, 16, 3);
            }
            case PLAY -> {
                for (int row = 4; row < 20; row++) {
                    float length = 13 * (1 - Math.abs(row + .5f - 12) / 8);
                    rectangle(graphics, 7, row, length, 1, color);
                }
            }
            case SERVER -> {
                box(graphics, 4, 4, 16, 6, color);
                box(graphics, 4, 14, 16, 6, color);
                line(graphics, 7, 7, 7.2f, 7, color);
                line(graphics, 7, 17, 7.2f, 17, color);
                line(graphics, 16, 7, 18, 7, color);
                line(graphics, 16, 17, 18, 17, color);
            }
            case WORLD -> {
                ellipse(graphics, 12, 12, 8, 8, color);
                ellipse(graphics, 12, 12, 4, 8, color);
                line(graphics, 4, 12, 20, 12, color);
            }
            case PEOPLE -> {
                ellipse(graphics, 9, 8, 3, 3, color);
                path(graphics, color, 3, 20, 3, 17, 5, 14, 9, 12, 13, 14, 15, 17, 15, 20);
                path(graphics, color, 16, 5, 18, 6, 19, 8, 18, 10, 16, 11);
                path(graphics, color, 18, 14, 20, 16, 21, 18, 21, 20);
            }
            case SETTINGS -> {
                for (int row : new int[] {6, 12, 18}) {
                    line(graphics, 4, row, 20, row, color);
                }
                line(graphics, 8, 3, 8, 9, color);
                line(graphics, 16, 9, 16, 15, color);
                line(graphics, 10, 15, 10, 21, color);
            }
            case EXIT -> {
                path(graphics, color, 10, 4, 4, 4, 4, 20, 10, 20);
                line(graphics, 9, 12, 21, 12, color);
                path(graphics, color, 17, 8, 21, 12, 17, 16);
            }
            case ARROW -> path(graphics, color, 9, 5, 16, 12, 9, 19);
            case BACK -> path(graphics, color, 16, 5, 9, 12, 16, 19);
            case PACK -> {
                path(graphics, color, 12, 3, 21, 8, 21, 17, 12, 22, 3, 17, 3, 8, 12, 3);
                path(graphics, color, 3, 8, 12, 13, 21, 8);
                line(graphics, 12, 13, 12, 22, color);
            }
            case FLASK -> {
                line(graphics, 8, 3, 16, 3, color);
                path(graphics, color, 10, 3, 10, 10, 4, 19, 5, 21, 19, 21, 20, 19, 14, 10, 14, 3);
                line(graphics, 7, 15, 17, 15, color);
            }
            case THEME -> {
                ellipse(graphics, 12, 12, 8, 8, color);
                for (int row = 4; row < 20; row++) {
                    float radius = (float) Math.sqrt(64 - Math.pow(row + .5f - 12, 2));
                    rectangle(graphics, 12 - radius, row, radius, 1, color);
                }
            }
            case ACCESSIBILITY -> {
                ellipse(graphics, 12, 4, 2, 2, color);
                line(graphics, 4, 9, 20, 9, color);
                path(graphics, color, 12, 7, 12, 14, 7, 21);
                line(graphics, 12, 14, 17, 21, color);
            }
        }
        graphics.pose().popMatrix();
    }

    private static void path(GuiGraphicsExtractor graphics, int color, float... points) {
        for (int i = 2; i < points.length; i += 2) {
            line(graphics, points[i - 2], points[i - 1], points[i], points[i + 1], color);
        }
    }

    private static void box(GuiGraphicsExtractor graphics, float x, float y, float width, float height, int color) {
        path(graphics, color, x, y, x + width, y, x + width, y + height, x, y + height, x, y);
    }

    private static void ellipse(GuiGraphicsExtractor graphics, float x, float y, float rx, float ry, int color) {
        for (int i = 0; i < 32; i++) {
            double start = i * Math.PI / 16;
            double end = (i + 1) * Math.PI / 16;
            line(graphics, x + rx * (float) Math.cos(start), y + ry * (float) Math.sin(start),
                x + rx * (float) Math.cos(end), y + ry * (float) Math.sin(end), color);
        }
    }

    private static void line(GuiGraphicsExtractor graphics, float x, float y, float endX, float endY, int color) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().rotate((float) Math.atan2(endY - y, endX - x));
        rectangle(graphics, -.5f, -.75f, (float) Math.hypot(endX - x, endY - y) + 1, 1.5f, color);
        graphics.pose().popMatrix();
    }

    private static void rectangle(GuiGraphicsExtractor graphics, float x, float y, float width, float height, int color) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(width, height);
        graphics.fill(0, 0, 1, 1, color);
        graphics.pose().popMatrix();
    }
}
