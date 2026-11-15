package dev.richasy.worldreroll.compat;

import com.terraformersmc.modmenu.api.ConfigScreenFactory;
import com.terraformersmc.modmenu.api.ModMenuApi;
import dev.richasy.worldreroll.RerollClientConfig;

/** Exposes this mod's own settings through the optional Fabric/Quilt mod list. */
public final class RerollModMenu implements ModMenuApi {
    /** Creates a local settings draft and returns to the actual mod list on close. */
    @Override public ConfigScreenFactory<?> getModConfigScreenFactory() { return RerollClientConfig::new; }
}
