package io.github.richasy.bettertools.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Share;
import com.llamalad7.mixinextras.sugar.ref.LocalRef;
import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeCombat;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeState;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.world.damagesource.DamageSource;
import net.minecraft.world.entity.Entity;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Native attacks/damage drive intent; only a transformation of the same cube avoids swap cooldown. */
@Mixin(Player.class)
abstract class CubeCombatMixin {
    @Inject(method = "itemAttackInteraction", at = @At("HEAD"))
    private void bettertools$rememberWeapon(Entity target, ItemStack weapon, DamageSource source, boolean effects, CallbackInfo callback,
            @Share("bettertools$attackCube") LocalRef<CubeState> before) {
        if (((Player)(Object)this).getMainHandItem() == weapon) before.set(weapon.get(BetterTools.STATE));
    }

    @WrapOperation(method = "itemAttackInteraction", at = @At(value = "INVOKE",
            target = "Lnet/minecraft/world/item/ItemStack;isEmpty()Z", ordinal = 1))
    private boolean bettertools$keepRecoveredCube(ItemStack weapon, Operation<Boolean> original,
            @Share("bettertools$attackCube") LocalRef<CubeState> before) {
        if (!original.call(weapon)) return false;
        var snapshot = before.get();
        var current = ((Player)(Object)this).getMainHandItem().get(BetterTools.STATE);
        // Recovery has already replaced the consumed original. Vanilla otherwise mistakes it for an
        // offhand weapon and clears that independent slot; only this same cube's recovery skips cleanup.
        return snapshot == null || current == null || !snapshot.identity().equals(current.identity());
    }

    @Inject(method = "attack", at = @At("HEAD"))
    private void bettertools$attack(Entity target, CallbackInfo callback) {
        if (target instanceof LivingEntity living && living.isAlive()) CubeCombat.attack((Player)(Object)this, living);
    }

    @Inject(method = "hurtServer", at = @At("RETURN"))
    private void bettertools$hurt(ServerLevel level, DamageSource source, float damage, CallbackInfoReturnable<Boolean> callback) {
        if (callback.getReturnValueZ() && source.getEntity() != null && source.getEntity() != (Object)this)
            CubeCombat.engage((Player)(Object)this, source.getEntity() instanceof LivingEntity living ? living : null);
    }

    @Redirect(method = "tick", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/item/ItemStack;isSameItem(Lnet/minecraft/world/item/ItemStack;Lnet/minecraft/world/item/ItemStack;)Z"))
    private boolean bettertools$keepCharge(ItemStack previous, ItemStack current) {
        return CubeInventory.sameCube(previous, current) || ItemStack.isSameItem(previous, current);
    }
}
