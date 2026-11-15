package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.ContainerInput;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Restores real tools to their shells before native inventory/bundle hooks process an action. */
@Mixin(AbstractContainerMenu.class)
abstract class InventoryInteractionMixin {
    @Inject(method = "clicked", at = @At("HEAD"))
    private void bettertools$interact(int index, int button, ContainerInput input, Player player, CallbackInfo callback) {
        var menu = (AbstractContainerMenu)(Object)this;
        // Stow before any native move/craft/anvil action, including hotbar keys and the carried cursor.
        var bag = player.getInventory();
        for (int i = 0; i < bag.getContainerSize(); i++) {
            var item = bag.getItem(i);
            if (CubeInventory.isCube(item)) bag.setItem(i, CubeInventory.idle(item));
        }
        if (CubeInventory.isCube(menu.getCarried())) menu.setCarried(CubeInventory.idle(menu.getCarried()));
    }
}
