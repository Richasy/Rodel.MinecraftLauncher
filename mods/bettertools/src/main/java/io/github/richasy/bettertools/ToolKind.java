package io.github.richasy.bettertools;

import net.minecraft.tags.ItemTags;
import net.minecraft.world.item.ItemStack;

/** The five user-visible columns, in the same order in both storage rows. */
public enum ToolKind {
    PICKAXE, AXE, SHOVEL, SWORD, HOE;

    /** Uses vanilla tool tags, including additions supplied by other mods or data packs. */
    public static ToolKind of(ItemStack stack) {
        if (stack.isEmpty() || CubeInventory.isCube(stack)) return null;
        if (stack.is(ItemTags.PICKAXES)) return PICKAXE;
        if (stack.is(ItemTags.AXES)) return AXE;
        if (stack.is(ItemTags.SHOVELS)) return SHOVEL;
        if (stack.is(ItemTags.SWORDS)) return SWORD;
        if (stack.is(ItemTags.HOES)) return HOE;
        return null;
    }

    /** Returns a stable translation key for an empty slot's shape tooltip. */
    public String translationKey() {
        return "bettertools.kind." + name().toLowerCase(java.util.Locale.ROOT);
    }
}
