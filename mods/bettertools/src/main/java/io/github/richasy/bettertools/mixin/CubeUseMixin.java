package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.CubeCombat;
import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;
import io.github.richasy.bettertools.CubeState;
import io.github.richasy.bettertools.NativeHoeUse;
import io.github.richasy.bettertools.ToolKind;
import net.minecraft.world.InteractionHand;
import net.minecraft.world.InteractionResult;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.UseOnContext;
import net.minecraft.world.phys.BlockHitResult;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** Tilling selects the real stored hoe before native use, instead of letting the mining shovel make a path. */
@Mixin(ItemStack.class)
abstract class CubeUseMixin {
    @Unique private CubeState bettertools$beforeUse;

    @Inject(method = "useOn", at = @At("HEAD"), cancellable = true)
    private void bettertools$till(UseOnContext context, CallbackInfoReturnable<InteractionResult> callback) {
        var player = context.getPlayer();
        var held = (ItemStack)(Object)this;
        bettertools$beforeUse = held.get(BetterTools.STATE);
        if (player == null || context.getHand() != InteractionHand.MAIN_HAND || !CubeInventory.isCube(held)
                || player.isSpectator() || player.containerMenu != player.inventoryMenu || CubeCombat.active(player)) return;
        var state = CubeInventory.state(held);
        var items = state.equipment(held);
        int hoe = CubeSelection.first(items, ToolKind.HOE.ordinal());
        if (hoe < 0 || state.active() == hoe) return;
        var hit = new BlockHitResult(context.getClickLocation(), context.getClickedFace(), context.getClickedPos(), context.isInside());
        var probe = new UseOnContext(context.getLevel(), player, context.getHand(), items.get(hoe), hit);
        if (!NativeHoeUse.canUse(items.get(hoe), probe)) return;
        CubeSelection.equip(player, hoe);
        callback.setReturnValue(player.getMainHandItem().useOn(new UseOnContext(player, context.getHand(), hit)));
        bettertools$beforeUse = null;
    }

    @Inject(method = "useOn", at = @At("RETURN"), cancellable = true)
    private void bettertools$keepReplacement(UseOnContext context, CallbackInfoReturnable<InteractionResult> callback) {
        var before = bettertools$beforeUse;
        bettertools$beforeUse = null;
        var player = context.getPlayer();
        if (before == null || player == null || !(callback.getReturnValue() instanceof InteractionResult.Success success)) return;
        var current = player.getItemInHand(context.getHand());
        var state = current.get(BetterTools.STATE);
        // Vanilla's post-use result can still point at the consumed stack. Preserve the actual original
        // installed by the native durability hook instead of letting the caller overwrite it with empty.
        if (current != (Object)this && state != null && state.identity().equals(before.identity()))
            callback.setReturnValue(success.heldItemTransformedTo(current));
    }
}
