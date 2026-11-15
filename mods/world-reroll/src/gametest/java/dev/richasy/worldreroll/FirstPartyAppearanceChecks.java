package dev.richasy.worldreroll;

import java.nio.file.Files;
import java.util.Properties;
import java.util.function.BooleanSupplier;
import java.util.function.Function;
import io.github.richasy.rodel.ai.AiClientConfig;
import io.github.richasy.bettertools.client.CubeClientConfig;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/**
 * Review: compilation and old UI screenshots cannot detect a shared theme, a lost draft or a non-durable setting.
 * Extend the existing real no-UI menu workflow with native buttons, real per-mod files and exact parent callbacks.
 * Every choice is restored; the Fabric test owns this game directory and never touches player configuration.
 */
final class FirstPartyAppearanceChecks {
    static void verify(ClientGameTestContext context, Screen parent) {
        boolean ai = context.computeOnClient(m -> AiClientConfig.dark());
        boolean reroll = context.computeOnClient(m -> RerollClientConfig.dark());
        boolean cube = context.computeOnClient(m -> CubeClientConfig.dark());
        verify(context, parent, "rai", "rodel-ai-client", AiClientConfig::new, AiClientConfig::dark, () -> RerollClientConfig.dark() == reroll && CubeClientConfig.dark() == cube);
        context.runOnClient(m -> check(RerollClientConfig.dark() == reroll && CubeClientConfig.dark() == cube, "AI settings changed another mod"));
        verify(context, parent, "worldreroll", "worldreroll-client", RerollClientConfig::new, RerollClientConfig::dark, () -> AiClientConfig.dark() == ai && CubeClientConfig.dark() == cube);
        context.runOnClient(m -> check(AiClientConfig.dark() == ai && CubeClientConfig.dark() == cube, "Reroll settings changed another mod"));
        verify(context, parent, "bettertools", "bettertools-client", CubeClientConfig::new, CubeClientConfig::dark, () -> AiClientConfig.dark() == ai && RerollClientConfig.dark() == reroll);
        context.runOnClient(m -> check(AiClientConfig.dark() == ai && RerollClientConfig.dark() == reroll, "Cube settings changed another mod"));
    }

    private static void verify(ClientGameTestContext context, Screen parent, String key, String file,
                               Function<Screen, Screen> factory, BooleanSupplier dark, BooleanSupplier isolated) {
        boolean initial = context.computeOnClient(m -> dark.getAsBoolean());
        context.setScreen(() -> factory.apply(parent));
        NativeTestButtons.click(context, label(context, key, initial));
        NativeTestButtons.click(context, translated(context, "gui.cancel"));
        context.runOnClient(m -> check(dark.getAsBoolean() == initial && m.gui.screen() == parent, "Cancel changed the theme or parent"));
        context.setScreen(() -> factory.apply(parent));
        NativeTestButtons.click(context, label(context, key, initial));
        NativeTestButtons.click(context, translated(context, "gui.done"));
        try {
            context.runOnClient(m -> {
                check(dark.getAsBoolean() != initial && m.gui.screen() == parent, "Done did not apply the independent theme");
                check(isolated.getAsBoolean(), "Changing this mod also changed another mod theme");
                var values = new Properties();
                try (var input = Files.newInputStream(m.gameDirectory.toPath().resolve("config/" + file + ".properties"))) { values.load(input); }
                catch (java.io.IOException error) { throw new AssertionError("Saved appearance is unreadable", error); }
                check(Boolean.toString(!initial).equals(values.getProperty("darkTheme")), "Done did not persist the actual theme");
            });
            context.setScreen(() -> factory.apply(parent));
            context.takeScreenshot(file + "-settings");
            NativeTestButtons.click(context, label(context, key, !initial));
            NativeTestButtons.click(context, translated(context, "gui.cancel"));
            context.runOnClient(m -> check(dark.getAsBoolean() != initial, "Reopened draft cancellation lost the saved theme"));
        } finally {
            context.setScreen(() -> factory.apply(parent));
            NativeTestButtons.click(context, label(context, key, !initial));
            NativeTestButtons.click(context, translated(context, "gui.done"));
        }
    }

    private static String translated(ClientGameTestContext context, String key) { return context.computeOnClient(m -> Component.translatable(key).getString()); }
    private static String label(ClientGameTestContext context, String key, boolean dark) {
        return context.computeOnClient(m -> Component.translatable(key + ".config.theme", Component.translatable(key + (dark ? ".config.dark" : ".config.light"))).getString());
    }
    private static void check(boolean value, String message) { if (!value) throw new AssertionError(message); }
}
