package io.github.richasy.bettertools.mixin;

import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import com.llamalad7.mixinextras.sugar.Share;
import com.llamalad7.mixinextras.sugar.ref.LocalRef;
import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeCombat;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeState;
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

/** 1.21.1 performs durability cleanup directly inside attack and exposes the original hurt callback. */
@Mixin(Player.class)
abstract class CubeCombatMixin {
    @Inject(method = "attack", at = @At("HEAD"))
    private void bettertools$attack(Entity target, CallbackInfo ci, @Share("bettertools$attackCube") LocalRef<CubeState> before) {
        var player = (Player)(Object)this;
        if (target instanceof LivingEntity living && living.isAlive()) CubeCombat.attack(player, living);
        before.set(player.getMainHandItem().get(BetterTools.STATE));
    }
    @WrapOperation(method = "attack", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/item/ItemStack;isEmpty()Z", ordinal = 1))
    private boolean bettertools$keepRecovered(ItemStack weapon, Operation<Boolean> original, @Share("bettertools$attackCube") LocalRef<CubeState> before) {
        if (!original.call(weapon)) return false;
        var snapshot = before.get();
        var current = ((Player)(Object)this).getMainHandItem().get(BetterTools.STATE);
        return snapshot == null || current == null || !snapshot.identity().equals(current.identity());
    }
    @Inject(method = "hurt", at = @At("RETURN"))
    private void bettertools$hurt(DamageSource source, float damage, CallbackInfoReturnable<Boolean> ci) {
        if (ci.getReturnValueZ() && source.getEntity() != null && source.getEntity() != (Object)this)
            CubeCombat.engage((Player)(Object)this, source.getEntity() instanceof LivingEntity living ? living : null);
    }
    @Redirect(method = "tick", at = @At(value = "INVOKE", target = "Lnet/minecraft/world/item/ItemStack;isSameItem(Lnet/minecraft/world/item/ItemStack;Lnet/minecraft/world/item/ItemStack;)Z"))
    private boolean bettertools$keepCharge(ItemStack previous, ItemStack current) {
        return CubeInventory.sameCube(previous, current) || ItemStack.isSameItem(previous, current);
    }
}
