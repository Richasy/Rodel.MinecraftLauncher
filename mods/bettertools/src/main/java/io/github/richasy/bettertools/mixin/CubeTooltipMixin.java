package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeTooltip;
import io.github.richasy.bettertools.client.ClientCubeTooltip;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent;
import net.minecraft.world.inventory.tooltip.TooltipComponent;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Converts only BetterTools' native tooltip image; all other tooltips retain their original factories. */
@Mixin(ClientTooltipComponent.class)
public interface CubeTooltipMixin {
    @Inject(method = "create(Lnet/minecraft/world/inventory/tooltip/TooltipComponent;)Lnet/minecraft/client/gui/screens/inventory/tooltip/ClientTooltipComponent;", at = @At("HEAD"), cancellable = true)
    private static void bettertools$tooltip(TooltipComponent component, CallbackInfoReturnable<ClientTooltipComponent> ci) {
        if (component instanceof CubeTooltip cube) ci.setReturnValue(new ClientCubeTooltip(cube));
    }
}
