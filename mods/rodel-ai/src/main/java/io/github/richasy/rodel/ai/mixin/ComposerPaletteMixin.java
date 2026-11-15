package io.github.richasy.rodel.ai.mixin;

import io.github.richasy.rodel.ai.AiChatScreen;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.MultiLineEditBox;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Follows the live palette only while Rodel AI owns the editor's screen. */
@Mixin(MultiLineEditBox.class)
public abstract class ComposerPaletteMixin {
    @Shadow @Final private int textColor;
    @Shadow @Final private int cursorColor;

    @Redirect(method = "extractContents", at = @At(value = "FIELD", target = "Lnet/minecraft/client/gui/components/MultiLineEditBox;textColor:I"))
    private int rai$text(MultiLineEditBox editor) {
        return Minecraft.getInstance().gui.screen() instanceof AiChatScreen screen ? screen.textColor() : textColor;
    }

    @Redirect(method = "extractContents", at = @At(value = "FIELD", target = "Lnet/minecraft/client/gui/components/MultiLineEditBox;cursorColor:I"))
    private int rai$cursor(MultiLineEditBox editor) {
        return Minecraft.getInstance().gui.screen() instanceof AiChatScreen screen ? screen.textColor() : cursorColor;
    }
}
