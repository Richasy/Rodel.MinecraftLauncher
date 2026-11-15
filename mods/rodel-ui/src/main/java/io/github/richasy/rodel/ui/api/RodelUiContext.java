package io.github.richasy.rodel.ui.api;

import java.util.Objects;
import java.util.function.Supplier;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuTheme;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.Screen;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.UiContexts;

/** An explicitly requested appearance. It never registers or changes a Screen. */
public record RodelUiContext(Supplier<MenuTheme> themeSupplier) implements MenuContext {
    public RodelUiContext {
        Objects.requireNonNull(themeSupplier, "themeSupplier");
    }

    public RodelUiContext(MenuTheme theme) {
        this(constantTheme(theme));
    }

    /** Borrows the parent's live appearance for an explicitly styled first-party surface, without owning its Screen. */
    public static RodelUiContext forScreen(Screen parent) {
        MenuContext source = UiContexts.owned(parent);
        if (source == null) {
            var client = Minecraft.getInstance();
            var window = client.getWindow();
            source = new TitleMenu(client, window.getGuiScaledWidth(), window.getGuiScaledHeight());
        }
        MenuContext appearance = source;
        return new RodelUiContext(appearance::theme);
    }

    private static Supplier<MenuTheme> constantTheme(MenuTheme theme) {
        Objects.requireNonNull(theme, "theme");
        return () -> theme;
    }

    @Override public float scale() { return MenuContext.designScale(); }

    @Override public boolean compact() {
        var window = Minecraft.getInstance().getWindow();
        return MenuContext.compact(window.getGuiScaledWidth(), window.getGuiScaledHeight());
    }

    @Override public MenuTheme theme() {
        return Objects.requireNonNull(themeSupplier.get(), "themeSupplier result");
    }
}
