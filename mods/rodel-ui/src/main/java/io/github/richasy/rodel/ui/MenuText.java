package io.github.richasy.rodel.ui;

import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.network.chat.MutableComponent;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;

public final class MenuText {
    private static final FontDescription CHINESE = new FontDescription.Resource(
        Identifier.fromNamespaceAndPath("rodel_game_ui", "chinese")
    );
    private static final FontDescription SECONDARY = new FontDescription.Resource(
        Identifier.fromNamespaceAndPath("rodel_game_ui", "regular")
    );

    private MenuText() {}

    public static Component secondary(Component message) {
        return regular(message, null);
    }

    public static Component secondary(Component message, int color) {
        return regular(message, color);
    }

    /** Uses the real system bold faces for short headings and labels, retaining semantic style colors. */
    public static Component emphasized(Component message) {
        MutableComponent result = Component.empty();
        message.visit((style, text) -> {
            result.append(Component.literal(text).setStyle(style.withFont(CHINESE).withBold(false)));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    /** Adapts native neutral text colors while retaining semantic colors such as errors and warnings. */
    public static Component themedSecondary(Component message, int neutralColor) {
        MutableComponent result = Component.empty();
        message.visit((style, text) -> {
            var color = style.getColor();
            boolean neutral = color == null || switch (color.getValue()) {
                case 0xFFFFFF, 0xE0E0E0, 0xAAAAAA, 0xA0A0A0, 0x808080, 0x555555 -> true;
                default -> false;
            };
            Style presentation = style.withFont(SECONDARY).withBold(false).withShadowColor(0);
            result.append(Component.literal(text).setStyle(neutral ? presentation.withColor(neutralColor) : presentation));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    private static Component regular(Component message, Integer color) {
        MutableComponent result = Component.empty();
        message.visit((style, text) -> {
            Style presentation = style.withFont(SECONDARY).withBold(false);
            if (color != null) presentation = presentation.withColor(color);
            result.append(Component.literal(text).setStyle(presentation));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    public static Component styled(Component message) {
        MutableComponent result = Component.empty();
        message.visit((style, text) -> {
            StringBuilder run = new StringBuilder();
            Boolean previousChinese = null;
            for (int codePoint : text.codePoints().toArray()) {
                boolean chinese = Character.UnicodeScript.of(codePoint) == Character.UnicodeScript.HAN
                    || codePoint >= 0x3000 && codePoint <= 0x303F
                    || codePoint >= 0xFF01 && codePoint <= 0xFF60;
                if (previousChinese != null && previousChinese != chinese) {
                    append(result, run, style, previousChinese);
                    run.setLength(0);
                }
                run.appendCodePoint(codePoint);
                previousChinese = chinese;
            }
            if (previousChinese != null) {
                append(result, run, style, previousChinese);
            }
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    private static void append(MutableComponent target, StringBuilder text, Style style, boolean chinese) {
        Style presentation = style.withBold(false);
        target.append(Component.literal(text.toString()).setStyle(chinese ? presentation.withFont(CHINESE) : presentation));
    }

    public static void extract(GuiGraphicsExtractor graphics, Font font, Component message,
                               float x, float y, float size, int color) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(size / font.lineHeight);
        graphics.text(font, message, 0, 0, color, false);
        graphics.pose().popMatrix();
    }
}
