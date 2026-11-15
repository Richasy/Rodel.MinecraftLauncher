package io.github.richasy.rodel.ai.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ChatScreen;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/** Older chat screens close directly after submission instead of reading closeOnSubmit. */
@Mixin(ChatScreen.class)
public abstract class ConsoleChatMixin {
    @Shadow protected EditBox input;

    @WrapOperation(method = "keyPressed", at = @At(value = "INVOKE", target = "Lnet/minecraft/client/Minecraft;setScreen(Lnet/minecraft/client/gui/screens/Screen;)V"))
    private void rai$keepConsole(Minecraft client, Screen screen, Operation<Void> original) {
        String command = input.getValue().strip();
        if (screen == null && (command.equals("/rai") || command.startsWith("/rai ") || command.startsWith("/rai_decide ") || command.equals("/rai_new"))) {
            input.setValue("");
            client.gui.getChat().resetChatScroll();
        } else original.call(client, screen);
    }
}
