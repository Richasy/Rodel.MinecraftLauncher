package io.github.richasy.rodel.ai.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.ChatScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

/** Keeps the player's existing chat open after AI commands; native Esc and ordinary chat keep their behavior. */
@Mixin(ChatScreen.class)
public abstract class ConsoleChatMixin {
    @Shadow protected EditBox input;

    @ModifyExpressionValue(method = "keyPressed", at = @At(value = "FIELD", target = "Lnet/minecraft/client/gui/screens/ChatScreen;closeOnSubmit:Z"))
    private boolean rai$keepConsole(boolean close) {
        String command = input.getValue().strip();
        return close && !(command.equals("/rai") || command.startsWith("/rai ") || command.startsWith("/rai_decide ") || command.equals("/rai_new"));
    }
}
