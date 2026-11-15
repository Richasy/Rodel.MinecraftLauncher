package io.github.richasy.rodel.ui;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.gui.screens.Screen;

public final class UiContexts {
    private UiContexts() {}

    public static MenuContext modern(Screen screen) {
        if (screen == null) return null;
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        if (page != null) return page;
        return screen instanceof ModernScreen modern ? modern.rodel$modernContext() : null;
    }

    public static MenuContext owned(Screen screen) {
        MenuContext context = modern(screen);
        if (context != null) return context;
        return screen instanceof StyledMenuScreen title ? title.rodel$context() : null;
    }
}
