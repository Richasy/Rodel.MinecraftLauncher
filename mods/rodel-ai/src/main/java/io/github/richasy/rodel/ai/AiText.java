package io.github.richasy.rodel.ai;

import java.util.Optional;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;

/** Small AI-only text fitting helper that retains the resource pack's native component styles. */
final class AiText {
    private AiText() { }
    record Layout(Font font, Component text, float height, float width, boolean trimmed) { }
    static Component regularText(Component text) { return text; }
    static Layout measureText(Font font, Component text, float size) { return new Layout(font, text, size, font.width(text) * size / font.lineHeight, false); }
    static Layout fitText(Font font, Component text, float size, float width) {
        var full = measureText(font, text, size);
        if (full.width() <= width) return full;
        int available = Math.max(0, (int) (width * font.lineHeight / size));
        Component ellipsis = Component.literal("…").setStyle(text.getStyle());
        var shown = Component.empty();
        if (available >= font.width(ellipsis)) {
            font.substrByWidth(text, available - font.width(ellipsis)).visit((style, value) -> {
                shown.append(Component.literal(value).setStyle(style)); return Optional.empty();
            }, text.getStyle());
            shown.append(ellipsis);
        }
        return new Layout(font, shown, size, font.width(shown) * size / font.lineHeight, true);
    }
    static void drawText(GuiGraphicsExtractor graphics, Layout text, float x, float y, int color) {
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x, y); graphics.pose().scale(text.height() / text.font().lineHeight);
            graphics.text(text.font(), text.text(), 0, 0, color, false);
        } finally { graphics.pose().popMatrix(); }
    }
}
