package dev.richasy.worldreroll.mixin;

import dev.richasy.worldreroll.VillageSpawn;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.damagesource.DamageSource;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Prevents damage while preparation keeps the world ticking, without saving a permanent player flag. */
@Mixin(ServerPlayer.class)
public abstract class VillagePlayerMixin {
    @Inject(method = "isInvulnerableTo", at = @At("HEAD"), cancellable = true)
    private void worldreroll$protectPreparation(ServerLevel level, DamageSource damage, CallbackInfoReturnable<Boolean> callback) {
        if (VillageSpawn.isPreparing((ServerPlayer) (Object) this)) callback.setReturnValue(true);
    }
}
