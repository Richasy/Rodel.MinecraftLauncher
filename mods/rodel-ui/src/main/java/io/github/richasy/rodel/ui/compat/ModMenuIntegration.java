package io.github.richasy.rodel.ui.compat;

import io.github.richasy.rodel.ui.platform.LoaderPlatform;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.toasts.SystemToast;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;

public final class ModMenuIntegration {
    private static final Logger LOGGER = LoggerFactory.getLogger("Rodel UI");

    private ModMenuIntegration() {}

    public static boolean installed() {
        return LoaderPlatform.isModLoaded("modmenu");
    }

    public static void open(Screen parent) {
        Minecraft minecraft = Minecraft.getInstance();
        try {
            if (!installed()) {
                throw new IllegalStateException("Mod Menu is not installed.");
            }
            minecraft.gui.setScreen(ModMenuScreens.create(parent));
        } catch (RuntimeException exception) {
            LOGGER.error("Could not open the original Mod Menu screen.", exception);
            SystemToast.add(minecraft.gui.toastManager(), SystemToast.SystemToastId.PACK_LOAD_FAILURE,
                Component.translatable("rodel.settings.mods.error"), Component.literal(exception.toString()));
        }
    }
}
