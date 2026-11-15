package io.github.richasy.rodel.ai;

import dev.richasy.worldreroll.fixtures.mixin.ChatHistoryAccess;
import net.fabricmc.fabric.api.client.gametest.v1.context.ClientGameTestContext;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.network.chat.Component;
import com.mojang.blaze3d.platform.InputConstants;

/**
 * Review: Core tests cannot detect vanilla chat closing after an AI command or a replacement retaining its old age.
 * Exercise the actual command, native Enter/Esc and chat Mixin in an isolated real client. A synthetic local status
 * remains pending beyond vanilla's 200-tick fade window; no model or launcher connection is claimed.
 */
public final class AiConsoleChecks {
    private AiConsoleChecks() {}

    public static void verify(ClientGameTestContext context) {
        var parent = context.computeOnClient(m -> m.gui.screen());
        var saved = context.computeOnClient(m -> m.gui.hud.getChat().storeState());
        var screen = context.computeOnClient(m -> new TestChat());
        Component pending = Component.literal("Synthetic delayed AI status");
        Component unrelated = Component.literal("Unrelated native message");
        try {
            context.setScreen(() -> screen);
            context.runOnClient(m -> {
                screen.submit("/rai controlled console question");
                check(m.gui.screen() == screen, "Submitting /rai closed the native console");
                check(screen.value().isEmpty(), "The submitted AI command remained in the input");
                screen.insertText("next draft", true);
                m.gui.hud.getChat().addClientSystemMessage(unrelated);
                m.gui.hud.getChat().addClientSystemMessage(pending);
            });
            int old = context.computeOnClient(m -> ageOf(unrelated));
            context.waitTicks(240);
            context.runOnClient(m -> {
                check(m.gui.screen() == screen && screen.value().equals("next draft"), "Waiting lost the open console or next draft");
                Component answer = Component.literal("Synthetic delayed AI answer");
                check(((ChatHistory) m.gui.hud.getChat()).rai$replace(pending, answer), "The exact delayed status could not be replaced");
                check(ageOf(answer) == m.gui.hud.getGuiTicks() && ageOf(answer) > old + 200, "A delayed AI result retained an expired message age");
                check(ageOf(unrelated) == old, "Refreshing AI changed an unrelated message's age");
            });
            context.takeScreenshot("first-party-ai-console-delayed");
            context.runOnClient(m -> screen.keyPressed(new KeyEvent(InputConstants.KEY_ESCAPE, 0, 0)));
            context.runOnClient(m -> check(m.gui.screen() == null, "Esc no longer closes AI console chat"));
            context.setScreen(TestChat::new);
            context.runOnClient(m -> {
                ((TestChat) m.gui.screen()).submit("/help");
                check(m.gui.screen() == null, "An ordinary command incorrectly kept chat open");
            });
            // BetterTools has no required mod-list UI; native chat must not close its newly opened configuration.
            context.setScreen(TestChat::new);
            context.runOnClient(m -> ((TestChat) m.gui.screen()).submit("/bettertools config"));
            context.waitForScreen(io.github.richasy.bettertools.client.CubeClientConfig.class);
            context.runOnClient(m -> m.gui.screen().onClose());
            context.runOnClient(m -> check(m.gui.screen() == null, "Cube configuration returned to a submitted chat screen"));
            context.setScreen(TestChat::new);
            context.runOnClient(m -> {
                ((TestChat) m.gui.screen()).submit("/rai");
                check(m.gui.screen() instanceof AiChatScreen, "Submitting bare /rai immediately closed its AI panel");
            });
        } finally {
            context.runOnClient(m -> m.gui.hud.getChat().restoreState(saved));
            context.setScreen(() -> parent);
        }
    }

    private static int ageOf(Component component) {
        return ((ChatHistoryAccess) Minecraft.getInstance().gui.hud.getChat()).worldreroll$messages().stream()
            .filter(message -> message.content() == component).findFirst().orElseThrow().addedTime();
    }

    private static final class TestChat extends ChatScreen {
        TestChat() { super("", false); }
        void submit(String command) { input.setValue(command); keyPressed(new KeyEvent(InputConstants.KEY_RETURN, 0, 0)); }
        String value() { return input.getValue(); }
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
