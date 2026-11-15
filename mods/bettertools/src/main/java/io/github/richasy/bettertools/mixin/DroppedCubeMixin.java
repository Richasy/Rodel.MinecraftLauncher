package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.world.entity.item.ItemEntity;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

/** Dropped and death-loot cubes return to their shell with the current tool safely stowed. */
@Mixin(ItemEntity.class)
abstract class DroppedCubeMixin {
    @ModifyVariable(method = "setItem", at = @At("HEAD"), argsOnly = true)
    private ItemStack bettertools$stow(ItemStack stack) { return CubeInventory.idle(stack); }
}
