package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeInventory;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.entity.player.Player;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Death treats the carried cube as storage, so an active tool's vanishing curse cannot delete the other seven tools. */
@Mixin(Player.class)
abstract class DeathStorageMixin {
    @Inject(method = "dropEquipment", at = @At("HEAD"))
    private void bettertools$stowBeforeDeath(ServerLevel level, CallbackInfo callback) {
        var bag = ((Player)(Object)this).getInventory();
        for (int i = 0; i < bag.getContainerSize(); i++) {
            var item = bag.getItem(i);
            if (CubeInventory.isCube(item)) bag.setItem(i, CubeInventory.idle(item));
        }
    }
}
