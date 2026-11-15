package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ArtifactBlueprintFonts;
import net.minecraft.network.chat.Style;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(targets = "net.minecraft.client.gui.Font$PreparedTextBuilder")
public abstract class BlueprintTextWeightMixin {
    @ModifyVariable(method = "accept", at = @At("HEAD"), argsOnly = true)
    private Style rodel$blueprintWeight(Style style) {
        return ArtifactBlueprintFonts.resolveWeight(style);
    }
}
