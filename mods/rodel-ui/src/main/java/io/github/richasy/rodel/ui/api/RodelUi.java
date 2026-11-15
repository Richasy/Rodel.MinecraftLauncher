package io.github.richasy.rodel.ui.api;

import java.util.Objects;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.controls.ButtonSurface;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.AlertScreen;
import net.minecraft.client.gui.screens.GenericMessageScreen;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.NativeDialog;
import io.github.richasy.rodel.ui.worlds.WorldBranchMenus;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;

/** Opt-in drawing primitives in native Minecraft GUI units; the caller owns layout and input. */
public final class RodelUi {
    private RodelUi() {}

    /** Explicitly styles one supplied native first-party waiting/error screen while retaining its model and callbacks. */
    public static Screen styleWorldDialog(Screen screen, RodelUiContext context) {
        Objects.requireNonNull(screen, "screen"); Objects.requireNonNull(context, "context");
        if (screen.getClass() == GenericMessageScreen.class) {
            if (!UiConfig.enabled(Effect.WORLDS)) return screen;
            ((SettingsHost) screen).rodel$rootContext(context);
            var host = (DialogHost) screen;
            if (host.rodel$dialog() == null) host.rodel$dialog(new NativeDialog(screen, context, false));
        } else if (screen.getClass() == AlertScreen.class) {
            if (!UiConfig.enabled(Effect.WORLDS)) return screen;
            ((SettingsHost) screen).rodel$rootContext(context);
            WorldBranchMenus.attach(screen, context);
        } else throw new IllegalArgumentException("Only exact native waiting/error screens are supported");
        return screen;
    }

    /** Keeps the same Font, styled text and size; explicit component fonts avoid context-dependent defaults. */
    public static final class TextLayout {
        private final Font font;
        private final Component text;
        private final float size;
        private final float width;
        private final boolean trimmed;

        private TextLayout(Font font, Component text, float size, boolean trimmed) {
            this.font = font;
            this.text = text.copy();
            this.size = size;
            this.width = font.width(this.text) * size / font.lineHeight;
            this.trimmed = trimmed;
        }

        public float width() { return width; }
        public float height() { return size; }
        public boolean trimmed() { return trimmed; }
    }

    public static int drawButtonSurface(GuiGraphicsExtractor graphics, RodelUiContext context,
                                        int x, int y, int width, int height,
                                        ButtonSurface.Role role, ButtonSurface.State state) {
        Objects.requireNonNull(graphics, "graphics");
        Objects.requireNonNull(context, "context");
        Objects.requireNonNull(role, "role");
        Objects.requireNonNull(state, "state");
        var theme = context.theme();
        if (width <= 0 || height <= 0) return theme.muted();
        float scale = context.scale();
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x, y);
            graphics.pose().scale(scale);
            return ButtonSurface.draw(graphics, theme, Math.round(width / scale), Math.round(height / scale), role, state);
        } finally {
            graphics.pose().popMatrix();
        }
    }

    public static TextLayout measureText(Font font, Component text, float size) {
        validateText(font, text, size);
        return new TextLayout(font, text, size, false);
    }

    /** Explicit regular Rodel font on every style run, stable across modern and ordinary Screen contexts. */
    public static Component regularText(Component text) {
        return MenuText.secondary(Objects.requireNonNull(text, "text"));
    }

    /** Explicit real system bold font for a caller's headings and option labels, without changing body text. */
    public static Component emphasizedText(Component text) {
        return MenuText.emphasized(Objects.requireNonNull(text, "text"));
    }

    /** Fits a single line while retaining component font/color/style runs. */
    public static TextLayout fitText(Font font, Component text, float size, float availableWidth) {
        validateText(font, text, size);
        if (!Float.isFinite(availableWidth) || availableWidth < 0)
            throw new IllegalArgumentException("availableWidth must be finite and nonnegative");
        TextLayout full = new TextLayout(font, text, size, false);
        if (full.width() <= availableWidth) return full;
        // Composite components often carry their explicit font on the first run, not the empty root.
        var ellipsisStyle = text.visit((style, value) -> value.isEmpty()
            ? java.util.Optional.empty() : java.util.Optional.of(style), text.getStyle()).orElse(text.getStyle());
        Component ellipsis = Component.literal("…").setStyle(ellipsisStyle);
        float scale = size / font.lineHeight;
        int available = Math.max(0, (int) Math.floor(availableWidth / scale));
        if (font.width(ellipsis) > available) return new TextLayout(font, Component.empty(), size, true);
        var shown = Component.empty();
        font.substrByWidth(text, available - font.width(ellipsis)).visit((style, value) -> {
            shown.append(Component.literal(value).setStyle(style));
            return java.util.Optional.empty();
        }, text.getStyle());
        shown.append(ellipsis);
        return new TextLayout(font, shown, size, true);
    }

    public static void drawText(GuiGraphicsExtractor graphics, TextLayout layout, float x, float y, int color) {
        Objects.requireNonNull(graphics, "graphics");
        Objects.requireNonNull(layout, "layout");
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x, y);
            graphics.pose().scale(layout.size / layout.font.lineHeight);
            graphics.text(layout.font, layout.text, 0, 0, color, false);
        } finally {
            graphics.pose().popMatrix();
        }
    }

    private static void validateText(Font font, Component text, float size) {
        Objects.requireNonNull(font, "font");
        Objects.requireNonNull(text, "text");
        if (!Float.isFinite(size) || size <= 0) throw new IllegalArgumentException("size must be finite and positive");
    }
}
