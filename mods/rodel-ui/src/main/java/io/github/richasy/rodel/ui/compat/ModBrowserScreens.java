package io.github.richasy.rodel.ui.compat;

import net.minecraft.client.gui.screens.Screen;

public final class ModBrowserScreens {
    private ModBrowserScreens() {}

    public static Screen modernize(Screen screen) {
        return screen != null && ModMenuIntegration.installed() ? ModMenuScreens.modernize(screen) : screen;
    }
}
