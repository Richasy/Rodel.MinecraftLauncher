package dev.richasy.worldreroll;

import io.github.richasy.rodel.ai.AiChatScreen;
import net.fabricmc.fabric.api.client.gametest.v1.FabricClientGameTest;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.WorldOptionsScreen;
import net.minecraft.client.gui.screens.options.OptionsScreen;
import net.minecraft.network.chat.Component;

/**
 * Review: isolated reroll tests and compilation cannot detect a missing standalone AI pause entry.
 * This real client loads AI and World Reroll without UI, preserves one world across pause/AI/return and exercises native
 * GUI scales. No launcher agent or model is substituted for a claimed AI answer; that boundary remains untested here.
 */
public final class FirstPartyMenusGameTest implements FabricClientGameTest {
    @Override
    public void runTest(ClientGameTestContext context) {
        check(!net.fabricmc.loader.api.FabricLoader.getInstance().isModLoaded("rodel_game_ui"), "This workflow must run without Rodel UI");
        var world = context.worldBuilder().create();
        try {
            var level = context.computeOnClient(m -> m.level);
            context.runOnClient(m -> {
                m.options.guiScale().set(3);
                m.options.pauseOnLostFocus = false;
                m.resizeGui();
            });
            var pause = context.computeOnClient(m -> new PauseScreen(true));
            context.setScreen(() -> pause);
            FirstPartyAppearanceChecks.verify(context, pause);
            NativeTestButtons.click(context, context.computeOnClient(m -> Component.translatable("rai.title").getString()));
            context.waitForScreen(AiChatScreen.class);
            context.waitTicks(10);
            context.runOnClient(m -> {
                check(m.level == level, "Opening AI replaced the active world");
                check(m.gui.screen().isPauseScreen(), "The AI panel did not retain native pause semantics");
            });
            context.takeScreenshot("first-party-ai-panel");
            context.runOnClient(m -> io.github.richasy.rodel.ai.AiTranscriptChecks.verify(m.font));
            context.runOnClient(m -> m.gui.screen().onClose());
            context.waitForScreen(PauseScreen.class);
            context.runOnClient(m -> {
                check(m.gui.screen() == pause, "Closing AI did not return to its actual parent");
                check(m.level == level, "Closing AI replaced the active world");
            });
            io.github.richasy.rodel.ai.AiConsoleChecks.verify(context);
            context.setScreen(() -> new WorldOptionsScreen(pause, net.minecraft.client.Minecraft.getInstance().level));
            context.waitTicks(5);
            context.runOnClient(m -> {
                var screen = m.gui.screen();
                screen.onClose();
            });
            context.waitForScreen(PauseScreen.class);
            NativeTestButtons.click(context, context.computeOnClient(m -> Component.translatable("menu.options").getString()));
            context.waitForScreen(OptionsScreen.class);
            context.waitTicks(5);
            context.takeScreenshot("first-party-native-settings");
            context.runOnClient(m -> m.gui.screen().onClose());
            org.slf4j.LoggerFactory.getLogger("worldreroll").info("FIRST_PARTY_MENUS_PASS: real pause/AI/return, native world settings and native options; no model answer claimed");
        } finally {
            world.close();
        }
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }

}
