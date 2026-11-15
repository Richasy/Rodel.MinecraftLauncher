package io.github.richasy.rodel.ui.compat;

import com.terraformersmc.modmenu.api.ModMenuApi;
import java.util.Objects;
import net.minecraft.client.gui.screens.Screen;

final class ModMenuScreens {
    private ModMenuScreens() {}

    static Screen create(Screen parent) {
        Screen original = Objects.requireNonNull(ModMenuApi.createModsScreen(parent), "Mod Menu returned no screen.");
        return ModernModsScreen.create(parent, original);
    }

    static Screen modernize(Screen screen) {
        return screen instanceof ModMenuParent mods
            ? ModernModsScreen.modernize(mods.rodel$previousScreen(), screen) : screen;
    }
}
