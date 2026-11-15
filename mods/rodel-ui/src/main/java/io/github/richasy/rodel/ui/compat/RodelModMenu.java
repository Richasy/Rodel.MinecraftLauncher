package io.github.richasy.rodel.ui.compat;

import com.terraformersmc.modmenu.api.ConfigScreenFactory;
import com.terraformersmc.modmenu.api.ModMenuApi;
import io.github.richasy.rodel.ui.config.UiConfigScreen;

/** Optional Fabric and Quilt Mod Menu configuration entrypoint. */
public final class RodelModMenu implements ModMenuApi {
    /** Opens the same settings screen as the in-game Options button. */
    @Override public ConfigScreenFactory<?> getModConfigScreenFactory() {
        return UiConfigScreen::new;
    }
}
