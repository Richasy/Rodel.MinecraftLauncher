package dev.richasy.worldreroll;

import java.nio.file.Files;
import com.mojang.blaze3d.platform.InputConstants;
import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import net.minecraft.world.level.storage.LevelResource;

/** Review: normal-size rerolls cannot detect clipped choices or an unreachable confirmation footer.
 * This minimum native 320×240 GUI exercises the footer and keyboard navigation on the same world and preferences.
 * It also runs without Rodel UI: no optional presentation classes are linked by the test itself. */
public final class RerollPresentationGameTest implements FabricClientGameTest {
    @Override public void runTest(ClientGameTestContext context) {
        int scale = context.computeOnClient(m -> m.options.guiScale().get());
        int oldWidth = context.computeOnClient(m -> m.getWindow().getWidth());
        int oldHeight = context.computeOnClient(m -> m.getWindow().getHeight());
        var world = context.worldBuilder().create();
        try {
            var level = context.computeOnClient(m -> m.level);
            var source = context.computeOnClient(m -> m.getSingleplayerServer().getWorldPath(LevelResource.ROOT));
            context.runOnClient(m -> { m.getWindow().setWindowed(1280, 960); m.options.guiScale().set(4); });
            context.waitFor(m -> m.getWindow().getWidth() == 1280 && m.getWindow().getHeight() == 960, 200);
            context.runOnClient(m -> m.resizeGui());
            var parent = context.computeOnClient(m -> new PauseScreen(true));
            var settings = context.computeOnClient(m -> new RerollSettingsScreen(parent));
            context.setScreen(() -> settings);
            context.runOnClient(m -> {
                check(settings.width == 320 && settings.height == 240, "The minimum native GUI was not reached: " + settings.width + "x" + settings.height);
                check(footer(settings, "gui.cancel").getBottom() <= settings.height, "Cancel is outside the viewport");
                check(footer(settings, "worldreroll.settings.continue").getBottom() <= settings.height, "Continue is outside the viewport");
                settings.keyPressed(new KeyEvent(InputConstants.KEY_PAGEDOWN, 0, 0));
                check(footer(settings, "worldreroll.settings.village").visible, "The second choice is inaccessible in the minimum GUI");
            });
            context.takeScreenshot("worldreroll-compact-settings");
            NativeTestButtons.click(context, Component.translatable("worldreroll.settings.continue").getString());
            context.waitForScreen(RerollConfirmScreen.class);
            context.runOnClient(m -> {
                check(footer(m.gui.screen(), "gui.cancel").getBottom() <= m.gui.screen().height, "Confirmation Cancel is clipped");
                check(footer(m.gui.screen(), "worldreroll.button").getBottom() <= m.gui.screen().height, "Confirmation action is clipped");
            });
            context.takeScreenshot("worldreroll-compact-confirm");
            context.runOnClient(m -> m.gui.screen().onClose());
            context.runOnClient(m -> check(m.gui.screen() == settings, "Cancel did not retain the actual preferences screen"));
            NativeTestButtons.click(context, Component.translatable("gui.cancel").getString());
            context.runOnClient(m -> check(m.gui.screen() == parent && m.level == level && Files.exists(source.resolve("level.dat")), "Cancel changed the active world or its source"));
        } finally {
            context.runOnClient(m -> { m.options.guiScale().set(scale); m.getWindow().setWindowed(oldWidth, oldHeight); m.resizeGui(); });
            world.close();
        }
    }

    private static Button footer(net.minecraft.client.gui.screens.Screen screen, String key) {
        String label = Component.translatable(key).getString();
        return screen.children().stream().filter(child -> child instanceof Button button && button.getMessage().getString().equals(label))
            .map(child -> (Button)child).findFirst().orElseThrow(() -> new AssertionError("Missing native button: " + key));
    }
    private static void check(boolean condition, String message) { if (!condition) throw new AssertionError(message); }
}
