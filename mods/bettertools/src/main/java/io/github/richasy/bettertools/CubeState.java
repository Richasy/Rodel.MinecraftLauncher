package io.github.richasy.bettertools;

import com.mojang.serialization.Codec;
import com.mojang.serialization.codecs.RecordCodecBuilder;
import java.util.UUID;
import net.minecraft.core.NonNullList;
import net.minecraft.core.UUIDUtil;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.component.ItemContainerContents;

/** Immutable storage snapshot. The active real stack lives in the player's hand, never in its own contents. */
public record CubeState(ItemContainerContents contents, int active, int display, int picked, UUID identity) {
    public static final int COLUMNS = 5;
    public static final int TOOL_SLOTS = 2 * COLUMNS;
    public static final int SHELL_SLOT = TOOL_SLOTS;
    public static final Codec<CubeState> CODEC = RecordCodecBuilder.create(instance -> instance.group(
            ItemContainerContents.CODEC.fieldOf("contents").forGetter(CubeState::contents),
            Codec.intRange(-1, TOOL_SLOTS - 1).fieldOf("active").forGetter(CubeState::active),
            Codec.intRange(-1, TOOL_SLOTS - 1).optionalFieldOf("display", -1).forGetter(CubeState::display),
            Codec.intRange(-1, TOOL_SLOTS - 1).optionalFieldOf("picked", -1).forGetter(CubeState::picked),
            UUIDUtil.CODEC.fieldOf("identity").forGetter(CubeState::identity),
            Codec.intRange(4, COLUMNS).optionalFieldOf("columns", 4).forGetter(state -> COLUMNS)
    ).apply(instance, CubeState::decode));

    // Player items with four columns keep their shell at index eight. Move both rows, shell and
    // selections together; growing the list alone would turn a reserve pickaxe into a hoe.
    private static CubeState decode(ItemContainerContents contents, int active, int display, int picked, UUID identity, int columns) {
        if (columns == COLUMNS) return new CubeState(contents, active, display, picked, identity);
        var previous = NonNullList.withSize(9, ItemStack.EMPTY);
        contents.copyInto(previous);
        var current = NonNullList.withSize(SHELL_SLOT + 1, ItemStack.EMPTY);
        for (int i = 0; i < 8; i++) current.set(remap(i), previous.get(i));
        current.set(SHELL_SLOT, previous.get(8));
        return new CubeState(ItemContainerContents.fromItems(current), remap(active), remap(display), remap(picked), identity);
    }

    private static int remap(int slot) { return slot < 4 ? slot : slot + 1; }

    /** Returns detached copies so inventory edits cannot mutate an already synchronized component. */
    public NonNullList<ItemStack> unpack() {
        var result = NonNullList.withSize(SHELL_SLOT + 1, ItemStack.EMPTY);
        contents.copyInto(result);
        return result;
    }

    /** Creates a fresh identity when an ordinary crafted cube first receives contents. */
    public static CubeState empty() {
        return new CubeState(ItemContainerContents.EMPTY, -1, -1, -1, UUID.randomUUID());
    }

    /** Copies the entire real held item back into its own slot for selection or editing. */
    public NonNullList<ItemStack> equipment(ItemStack held) {
        var items = unpack();
        var original = held.copy();
        original.remove(BetterTools.STATE);
        items.set(active < 0 ? SHELL_SLOT : active, original);
        return items;
    }
}
