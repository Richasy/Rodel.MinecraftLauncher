package io.github.richasy.rodel.ui.compat;

import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.MenuText;
import java.util.Optional;
import net.minecraft.client.gui.Font;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.FormattedText;
import net.minecraft.network.chat.Style;
import net.minecraft.util.FormattedCharSequence;

public final class ModBrowserText {
    private ModBrowserText() {}

    public static Component secondary(FormattedText text) {
        Component component;
        if (text instanceof Component message) component = message;
        else {
            var result = Component.empty();
            text.visit((style, run) -> {
                result.append(Component.literal(run).setStyle(style));
                return Optional.empty();
            }, Style.EMPTY);
            component = result;
        }
        return MenuText.secondary(component);
    }

    public static FormattedCharSequence secondary(FormattedCharSequence text) {
        Style[] regular = {Style.EMPTY};
        MenuText.secondary(Component.literal(" ")).visit((style, run) -> {
            regular[0] = style;
            return Optional.empty();
        }, Style.EMPTY);
        return sink -> text.accept((index, style, codePoint) ->
            sink.accept(index, style.withFont(regular[0].getFont()).withBold(false), codePoint));
    }

    public static FormattedCharSequence fitVisual(FormattedCharSequence text, Font font, int width) {
        return sink -> {
            float[] used = {0};
            boolean[] accepted = {true};
            text.accept((index, style, codePoint) -> {
                float advance = font.getSplitter().stringWidth(FormattedCharSequence.codepoint(codePoint, style));
                if (used[0] + advance > width) return false;
                used[0] += advance;
                accepted[0] = sink.accept(index, style, codePoint);
                return accepted[0];
            });
            return accepted[0];
        };
    }

    public static FormattedCharSequence themed(FormattedCharSequence text, MenuTheme theme) {
        return sink -> text.accept((index, style, codePoint) -> {
            var color = style.getColor();
            if (style.isUnderlined()) style = style.withColor(theme.focus() & 0xFFFFFF);
            else if (color != null) {
                int rgb = color.getValue();
                if (theme.highContrast() || rgb == 0xFFFFFF) style = style.withColor(theme.ink() & 0xFFFFFF);
                else if (rgb == 0xAAAAAA || rgb == 0x808080) style = style.withColor(theme.muted() & 0xFFFFFF);
            }
            return sink.accept(index, style, codePoint);
        });
    }
}
