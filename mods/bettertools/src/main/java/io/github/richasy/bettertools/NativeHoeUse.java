package io.github.richasy.bettertools;

import net.minecraft.core.component.DataComponents;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.context.UseOnContext;

/** Reads the actual hoe's native transformation rules; the original item still performs and validates the use. */
public final class NativeHoeUse {
    private NativeHoeUse() { }

    /** Checks native face, soil and space rules before choosing the stored hoe for this right-click. */
    public static boolean canUse(ItemStack hoe, UseOnContext context) {
        var player = context.getPlayer();
        if (player != null && player.getOffhandItem().has(DataComponents.BLOCKS_ATTACKS) && !player.isSecondaryUseActive()) return false;
        var transformer = hoe.get(DataComponents.BLOCK_TRANSFORMER);
        if (transformer == null) return false;
        for (var transform : transformer.value().transforms()) {
            if (!transform.disallowedFaces().contains(context.getClickedFace())
                    && transform.blockStateProvider().value().getOptionalState(context.getLevel(), context.getLevel().getRandom(), context.getClickedPos()) != null) return true;
        }
        return false;
    }
}
