package io.github.richasy.rodel.ai.mixin;

import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Keeps only AI submissions open while preserving the native command handler, history, and Escape. */
@Mixin(ChatScreen.class)
public abstract class ConsoleChatMixin {
    @Shadow protected EditBox input;

    @Redirect(method = "keyPressed", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/Minecraft;setScreen(Lnet/minecraft/client/gui/screens/Screen;)V"))
    private void rai$keepConsole(Minecraft client, Screen screen) {
        String command = input.getValue().strip();
        if (screen == null && (command.equals("/rai") || command.startsWith("/rai ") || command.startsWith("/rai_decide ") || command.equals("/rai_new"))) {
            input.setValue("");
            client.gui.getChat().resetChatScroll();
        } else client.setScreen(screen);
    }
}
