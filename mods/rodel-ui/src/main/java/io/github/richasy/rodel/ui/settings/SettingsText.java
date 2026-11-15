package io.github.richasy.rodel.ui.settings;

import io.github.richasy.rodel.ui.mixin.TooltipTextAccessor;
import io.github.richasy.rodel.ui.mixin.WidgetTooltipAccessor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.contents.TranslatableContents;
import net.minecraft.network.chat.Style;
import net.minecraft.ChatFormatting;
import io.github.richasy.rodel.ui.MenuTheme;

public final class SettingsText {
    private static final Style WHITE = Component.literal("").withStyle(ChatFormatting.WHITE).getStyle();
    private SettingsText() {}

    public static boolean hasKey(Component message, String key) {
        return message.getContents() instanceof TranslatableContents text && text.getKey().equals(key);
    }

    public static Component caption(Component source) {
        if (hasKey(source, "options.guiScale")) return Component.translatable("rodel.settings.gui_scale.label");
        if (hasKey(source, "options.fullscreen.exclusive.mode")) return Component.translatable("rodel.settings.resolution.label");
        return source;
    }

    public static Component label(Component source) {
        String text = source.getString();
        while (text.endsWith("…") || text.endsWith("...")) {
            text = text.substring(0, text.length() - (text.endsWith("…") ? 1 : 3));
        }
        return Component.literal(text);
    }

    public static Component description(AbstractWidget widget) {
        Tooltip tooltip = ((WidgetTooltipAccessor) widget).rodel$tooltip().get();
        Component original = tooltip == null ? Component.empty() : ((TooltipTextAccessor) tooltip).rodel$message();
        if (widget instanceof NativeOptionPresentation option && hasKey(option.rodel$caption(), "options.guiScale")) {
            var explanation = Component.translatable("rodel.settings.gui_scale.description");
            return original.getString().isBlank() ? explanation : explanation.append("\n").append(original);
        }
        return original;
    }

    public static Component bindingValue(Component source, MenuTheme theme) {
        var result = Component.empty();
        source.visit((style, text) -> {
            Style presentation = style.getColor() == null ? style
                : style.withColor(style.getColor().equals(WHITE.getColor()) ? theme.ink() : theme.focus());
            result.append(Component.literal(text).setStyle(presentation));
            return java.util.Optional.empty();
        }, Style.EMPTY);
        return result;
    }

    public static Component value(AbstractWidget widget) {
        if (widget instanceof Button && !(widget instanceof NativeOptionPresentation)) {
            return Component.translatable("rodel.settings.open");
        }
        Component message = widget instanceof NativeOptionPresentation presentation
            ? presentation.rodel$valueText() : widget.getMessage();
        return formattedValue(message);
    }

    public static Component choiceLabel(Component caption, Object value, Component original) {
        if (hasKey(caption, "options.guiScale") && value instanceof Integer multiplier && multiplier > 0) {
            return Component.translatable("rodel.settings.gui_scale.multiplier", multiplier);
        }
        return formattedValue(original);
    }

    public static Component formattedValue(Component message) {
        // Preserve the game's formatted argument; do not parse translated text or recalculate values.
        if (message.getContents() instanceof TranslatableContents text) {
            Object[] arguments = text.getArgs();
            if (text.getKey().equals("options.chunkFade.seconds") && arguments.length == 1) {
                return Component.translatable("rodel.settings.seconds", arguments[0]);
            }
            if (text.getKey().equals("options.chunkFade.none")) {
                return Component.translatable("rodel.settings.none");
            }
            if (arguments.length == 2 && (text.getKey().equals("options.generic_value")
                || text.getKey().equals("options.percent_value") || text.getKey().equals("options.pixel_value"))) {
                if (text.getKey().equals("options.percent_value")) {
                    return Component.translatable("rodel.settings.percent", arguments[1]);
                }
                if (text.getKey().equals("options.pixel_value")) {
                    return Component.translatable("rodel.settings.pixels", arguments[1]);
                }
                return arguments[1] instanceof Component value ? value : Component.literal(String.valueOf(arguments[1]));
            }
        }
        return message;
    }
}
