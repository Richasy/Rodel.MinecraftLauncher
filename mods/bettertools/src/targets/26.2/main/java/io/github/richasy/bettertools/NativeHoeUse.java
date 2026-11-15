package io.github.richasy.bettertools;

import io.github.richasy.bettertools.mixin.HoeUseAccess;
import net.minecraft.world.item.HoeItem;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.UseOnContext;

/** Before block-transformer components, vanilla hoes expose soil predicates through their native tillable map. */
public final class NativeHoeUse {
    private NativeHoeUse() { }

    /** Uses the actual vanilla/mod-extended soil predicate, leaving execution and durability to the real hoe. */
    public static boolean canUse(ItemStack hoe, UseOnContext context) {
        if (!(hoe.getItem() instanceof HoeItem)) return false;
        var action = HoeUseAccess.bettertools$tillables().get(context.getLevel().getBlockState(context.getClickedPos()).getBlock());
        return action != null && action.getFirst().test(context);
    }
}
