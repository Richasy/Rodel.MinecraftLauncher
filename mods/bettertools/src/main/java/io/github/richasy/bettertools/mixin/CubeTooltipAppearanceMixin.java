package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeTooltip;
import io.github.richasy.bettertools.client.CubeTooltipLook;
import java.util.List;
import java.util.Optional;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/** Selects appearance through native tooltip arguments; never writes tooltip styles onto the real item. */
@Mixin(GuiGraphicsExtractor.class)
public abstract class CubeTooltipAppearanceMixin {
    @ModifyVariable(method = "setTooltipForNextFrame(Lnet/minecraft/client/gui/Font;Ljava/util/List;Ljava/util/Optional;IILnet/minecraft/resources/Identifier;Z)V",
            at = @At("HEAD"), argsOnly = true)
    private Identifier bettertools$style(Identifier original, Font font, List<Component> lines,
            Optional<TooltipComponent> image, int x, int y, Identifier style, boolean itemTooltip) {
        return image.orElse(null) instanceof CubeTooltip ? CubeTooltipLook.STYLE : original;
    }

    @ModifyVariable(method = "setTooltipForNextFrame(Lnet/minecraft/client/gui/Font;Ljava/util/List;Ljava/util/Optional;IILnet/minecraft/resources/Identifier;Z)V",
            at = @At("HEAD"), argsOnly = true)
    private List<Component> bettertools$text(List<Component> original, Font font, List<Component> lines,
            Optional<TooltipComponent> image, int x, int y, Identifier style, boolean itemTooltip) {
        if (!(image.orElse(null) instanceof CubeTooltip)) return original;
        // The single image component owns the approved header/card and its separate detail bubble.
        // Forge inserts the image at index one. Retain its native title slot without painting a duplicate title.
        return List.of(Component.empty());
    }
}
