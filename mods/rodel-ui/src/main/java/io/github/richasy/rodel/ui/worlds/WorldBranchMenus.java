package io.github.richasy.rodel.ui.worlds;

import java.util.Set;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import net.minecraft.client.gui.screens.Screen;

public final class WorldBranchMenus {
    private static final Set<String> EDITORS = Set.of(
        "net.minecraft.client.gui.screens.CreateFlatWorldScreen",
        "net.minecraft.client.gui.screens.PresetFlatWorldScreen",
        "net.minecraft.client.gui.screens.CreateBuffetWorldScreen",
        "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen$DetailsScreen");
    private static final Set<String> MESSAGES = Set.of(
        "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen",
        "net.minecraft.client.gui.screens.AlertScreen",
        "net.minecraft.client.gui.screens.DatapackLoadFailureScreen",
        "net.minecraft.client.gui.screens.RecoverWorldDataScreen",
        "net.minecraft.client.gui.screens.FileFixerAbortedScreen",
        "net.minecraft.client.gui.screens.worldselection.FileFixerProgressScreen");

    private WorldBranchMenus() {}

    public static boolean attach(Screen screen, MenuContext source) {
        if (screen == null || source == null || !UiConfig.enabled(Effect.WORLDS)) return false;
        String type = screen.getClass().getName();
        if (!EDITORS.contains(type) && !MESSAGES.contains(type)) return false;
        var host = (WorldPageHost) screen;
        if (host.rodel$worldPage() == null)
            host.rodel$worldPage(new WorldBranchPage(screen, source, EDITORS.contains(type)));
        return host.rodel$worldPage() instanceof WorldBranchPage;
    }
}
