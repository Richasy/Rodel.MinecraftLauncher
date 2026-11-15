package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeState;
import java.util.function.Consumer;
import net.minecraft.server.level.ServerLevel;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.item.ItemStack;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Dynamic;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Keeps the container alive when vanilla consumes the real active tool at its durability boundary. */
@Mixin(ItemStack.class)
abstract class ToolBreakMixin {
    @Unique private CubeState bettertools$beforeDamage;

    @Inject(method = "hurtAndBreak(ILnet/minecraft/server/level/ServerLevel;Lnet/minecraft/server/level/ServerPlayer;Ljava/util/function/Consumer;)V", at = @At("HEAD"))
    private void bettertools$remember(int damage, ServerLevel level, ServerPlayer user, Consumer<ItemStack> onBreak, CallbackInfo callback) {
        // Native entity/hand overloads delegate here, while custom mining can enter here directly.
        // Only the actual held original owns this hand; damaging a detached copy must not duplicate a cube.
        bettertools$beforeDamage = user != null && user.getMainHandItem() == (Object)this
                ? ((ItemStack)(Object)this).get(BetterTools.STATE) : null;
    }

    @Inject(method = "hurtAndBreak(ILnet/minecraft/server/level/ServerLevel;Lnet/minecraft/server/level/ServerPlayer;Ljava/util/function/Consumer;)V", at = @At("RETURN"))
    private void bettertools$recover(int damage, ServerLevel level, ServerPlayer user, Consumer<ItemStack> onBreak, CallbackInfo callback) {
        var snapshot = bettertools$beforeDamage;
        bettertools$beforeDamage = null;
        if (user != null && (user.getMainHandItem() == (Object)this || user.getMainHandItem().isEmpty()))
            CubeInventory.broken((ItemStack)(Object)this, snapshot, user, EquipmentSlot.MAINHAND);
    }

    // NeoForge's entity/hand overloads enter its LivingEntity extension directly, bypassing vanilla's
    // ServerPlayer bridge. The same real-held-item guard and recovery cover both paths; other loaders lack this overload.
    @Dynamic("NeoForge adds the LivingEntity durability overload")
    @Inject(method = "hurtAndBreak(ILnet/minecraft/server/level/ServerLevel;Lnet/minecraft/world/entity/LivingEntity;Ljava/util/function/Consumer;)V",
            at = @At("HEAD"), require = 0)
    private void bettertools$rememberEntity(int damage, ServerLevel level, LivingEntity user, Consumer<ItemStack> onBreak, CallbackInfo callback) {
        if (user instanceof ServerPlayer player) bettertools$remember(damage, level, player, onBreak, callback);
    }

    @Dynamic("NeoForge adds the LivingEntity durability overload")
    @Inject(method = "hurtAndBreak(ILnet/minecraft/server/level/ServerLevel;Lnet/minecraft/world/entity/LivingEntity;Ljava/util/function/Consumer;)V",
            at = @At("RETURN"), require = 0)
    private void bettertools$recoverEntity(int damage, ServerLevel level, LivingEntity user, Consumer<ItemStack> onBreak, CallbackInfo callback) {
        if (user instanceof ServerPlayer player) bettertools$recover(damage, level, player, onBreak, callback);
    }
}
