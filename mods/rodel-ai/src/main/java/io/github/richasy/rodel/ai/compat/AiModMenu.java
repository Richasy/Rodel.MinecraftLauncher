package io.github.richasy.rodel.ai.compat;

import com.terraformersmc.modmenu.api.ConfigScreenFactory;
import com.terraformersmc.modmenu.api.ModMenuApi;
import io.github.richasy.rodel.ai.AiClientConfig;

/** Exposes this mod's own settings through the optional Fabric/Quilt mod list. */
public final class AiModMenu implements ModMenuApi {
    /** Creates a local settings draft and returns to the actual mod list on close. */
    @Override public ConfigScreenFactory<?> getModConfigScreenFactory() { return AiClientConfig::new; }
}
