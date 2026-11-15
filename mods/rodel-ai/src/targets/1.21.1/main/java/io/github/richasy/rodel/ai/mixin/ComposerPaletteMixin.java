package io.github.richasy.rodel.ai.mixin;

import io.github.richasy.rodel.ai.AiChatScreen;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.MultiLineEditBox;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.ModifyConstant;

/** 1.21.1 uses literal editor colors; only the AI-owned editor follows its own live palette. */
@Mixin(MultiLineEditBox.class)
public abstract class ComposerPaletteMixin {
    @ModifyConstant(method = "renderContents", constant = { @Constant(intValue = -2039584), @Constant(intValue = -3092272) })
    private int rai$color(int original) {
        return Minecraft.getInstance().screen instanceof AiChatScreen screen ? screen.textColor() : original;
    }
}
