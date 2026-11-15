package io.github.richasy.rodel.ui.settings;

import io.github.richasy.rodel.ui.MenuContext;
import net.minecraft.client.gui.screens.Screen;

public interface SettingsHost {
    NativeSettingsPage rodel$settingsPage();
    void rodel$settingsPage(NativeSettingsPage page);
    MenuContext rodel$rootContext();
    void rodel$rootContext(MenuContext context);
    Screen rodel$nativeParent();
    void rodel$nativeParent(Screen parent);
}
