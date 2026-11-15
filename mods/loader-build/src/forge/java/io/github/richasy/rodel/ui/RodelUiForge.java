package io.github.richasy.rodel.ui;

import io.github.richasy.rodel.ui.config.UiConfigScreen;
import net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext;

/** Registers the shared native UI mixins and resources with Forge. */
@Mod("rodel_game_ui")
public final class RodelUiForge {
    /** Registers the configuration action in Forge's mod list. */
    public RodelUiForge(FMLJavaModLoadingContext context) {
        context.registerExtensionPoint(ConfigScreenFactory.class, () -> new ConfigScreenFactory(UiConfigScreen::new));
    }
}
