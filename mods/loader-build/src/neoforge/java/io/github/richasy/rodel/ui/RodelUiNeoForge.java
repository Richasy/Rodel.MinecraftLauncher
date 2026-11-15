package io.github.richasy.rodel.ui;

import io.github.richasy.rodel.ui.config.UiConfigScreen;
import net.neoforged.api.distmarker.Dist;
import net.neoforged.fml.ModContainer;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.client.gui.IConfigScreenFactory;

/** Registers the same native UI mixins and resources with NeoForge. */
@Mod(value = "rodel_game_ui", dist = Dist.CLIENT)
public final class RodelUiNeoForge {
    /** Registers the configuration action in NeoForge's mod list. */
    public RodelUiNeoForge(ModContainer container) {
        container.registerExtensionPoint(IConfigScreenFactory.class, (mod, parent) -> new UiConfigScreen(parent));
    }
}
