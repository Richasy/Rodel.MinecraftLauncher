package io.github.richasy.bettertools.checks;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;
import io.github.richasy.bettertools.CubeState;

import com.mojang.serialization.JsonOps;
import net.minecraft.core.component.DataComponents;
import net.minecraft.core.registries.Registries;
import net.minecraft.nbt.CompoundTag;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.EquipmentSlot;
import net.minecraft.world.item.ItemStack;
import net.minecraft.world.item.Items;
import net.minecraft.world.item.component.CustomData;
import net.minecraft.world.item.enchantment.Enchantments;
import net.minecraft.world.level.block.Blocks;

/** Shared real-server invariants; each loader test invokes these against its actual registered items and mixins. */
public final class CubeStorageChecks {
    private CubeStorageChecks() { }

    /** Full component equality and an actual native break distinguish lossless storage from copying only item IDs or damage. */
    public static void run(net.minecraft.server.level.ServerPlayer player) {
        consecutiveMiningBreaks(player, false);
        consecutiveMiningBreaks(player, true);
        hoesAndExistingItems(player);
        var cube = new ItemStack(BetterTools.CUBE);
        var pick = new ItemStack(Items.IRON_PICKAXE);
        pick.setDamageValue(pick.getMaxDamage() - 1);
        pick.set(DataComponents.CUSTOM_NAME, Component.literal("Original pick"));
        var tag = new CompoundTag();
        tag.putString("foreign_mod_value", "must survive");
        pick.set(DataComponents.CUSTOM_DATA, CustomData.of(tag));
        pick.set(DataComponents.ATTRIBUTE_MODIFIERS, pick.getOrDefault(DataComponents.ATTRIBUTE_MODIFIERS,
                net.minecraft.world.item.component.ItemAttributeModifiers.EMPTY).withModifierAdded(
                        net.minecraft.world.entity.ai.attributes.Attributes.ATTACK_DAMAGE,
                        new net.minecraft.world.entity.ai.attributes.AttributeModifier(BetterTools.id("test_foreign_bonus"),
                                2, net.minecraft.world.entity.ai.attributes.AttributeModifier.Operation.ADD_VALUE),
                        net.minecraft.world.entity.EquipmentSlotGroup.MAINHAND));
        pick.enchant(player.registryAccess().lookupOrThrow(Registries.ENCHANTMENT).getOrThrow(Enchantments.FORTUNE), 3);
        var expected = pick.copy();
        check(CubeInventory.insert(cube, pick) && pick.isEmpty(), "Primary insertion failed");
        var reserve = new ItemStack(Items.DIAMOND_PICKAXE);
        reserve.setDamageValue(17);
        var expectedReserve = reserve.copy();
        check(CubeInventory.insert(cube, reserve) && reserve.isEmpty(), "Reserve insertion failed");
        // GUI-only projection must track the exact original after shell restoration and native break.
        check(cube.isBarVisible() && cube.getBarWidth() == expected.getBarWidth()
                && cube.getBarColor() == expected.getBarColor(), "Idle shell displayed a synthetic or reserve durability bar");
        var axe = new ItemStack(Items.IRON_AXE);
        check(CubeInventory.insert(cube, axe), "Axe insertion failed");
        var ops = player.registryAccess().createSerializationContext(JsonOps.INSTANCE);
        var saved = ItemStack.CODEC.encodeStart(ops, cube).getOrThrow();
        cube = ItemStack.CODEC.parse(ops, saved).getOrThrow();
        check(ItemStack.isSameItemSameComponents(expected, CubeInventory.state(cube).unpack().get(0)), "Persisted pick lost components");
        check(ItemStack.isSameItemSameComponents(expectedReserve, CubeInventory.state(cube).unpack().get(5)), "Persisted reserve changed durability");
        var equipment = CubeInventory.state(cube).equipment(cube);
        check(CubeSelection.block(equipment, Blocks.STONE.defaultBlockState()) == 0, "Better reserve displaced usable primary");
        check(CubeSelection.weapon(equipment) == 1, "Missing sword did not fall back to axe");
        player.setItemSlot(EquipmentSlot.MAINHAND, CubeInventory.select(cube, 0));
        var active = player.getMainHandItem();
        active.hurtAndBreak(1, player, EquipmentSlot.MAINHAND);
        var replacement = player.getMainHandItem();
        check(replacement.is(Items.DIAMOND_PICKAXE), "Native break destroyed container or failed to equip reserve");
        check(replacement.getDamageValue() == 17, "Reserve inherited broken primary's damage");
        var returned = CubeInventory.idle(replacement);
        check(returned.is(BetterTools.CUBE), "Idle failed to restore shell");
        check(ItemStack.isSameItemSameComponents(expectedReserve, CubeInventory.displayedTool(returned)), "Idle icon forgot active reserve");
        check(returned.getBarWidth() == expectedReserve.getBarWidth()
                && returned.getBarColor() == expectedReserve.getBarColor(), "Shell retained broken primary's durability bar");
        var remaining = CubeInventory.state(returned).unpack();
        check(remaining.get(0).isEmpty() && ItemStack.isSameItemSameComponents(expectedReserve, remaining.get(5)), "Broken primary resurrected or reserve changed");
        check(remaining.get(1).is(Items.IRON_AXE), "Unrelated tool lost on primary break");
        // Review: after primary break the opposite cell is empty. Promotion must move the complete
        // reserve, retain its displayed durability and reverse exactly; filled-pair checks miss this path.
        var beforeSwap = returned.copy();
        CubeInventory.pick(returned, 5);
        check(CubeInventory.swapPicked(returned), "Single reserve could not become primary");
        var promoted = CubeInventory.state(returned);
        check(promoted.picked() == 0 && promoted.display() == 0 && promoted.unpack().get(5).isEmpty()
                && ItemStack.matches(expectedReserve, promoted.unpack().get(0)), "Empty-cell promotion lost the original or selection");
        check(returned.getBarWidth() == expectedReserve.getBarWidth(), "Promotion changed original durability projection");
        check(CubeInventory.swapPicked(returned), "Promoted tool could not return to reserve");
        CubeInventory.pick(returned, -1);
        check(ItemStack.matches(beforeSwap, returned), "Single-tool swap failed to preserve all cube state");
        check(!CubeInventory.swapPicked(returned), "Tab without an explicit selection reordered tools");
        nativeBundleClicks(player, returned, expectedReserve);
    }

    /** Review: a full iron cube must survive consecutive primary/reserve breaks and eventual exhaustion.
     * Existing checks break a primary once, then stop. Native mining must remove only the consumed
     * original at every boundary, preserving the shell identity and every other complete component set.
     * Custom mining can call the server-level durability overload directly; the entity overload alone misses it. */
    private static void consecutiveMiningBreaks(net.minecraft.server.level.ServerPlayer player, boolean callbackDamage) {
        var previousMode = player.gameMode.getGameModeForPlayer();
        player.setGameMode(net.minecraft.world.level.GameType.SURVIVAL);
        player.getInventory().clearContent();
        var cube = new ItemStack(BetterTools.CUBE);
        var expected = net.minecraft.core.NonNullList.withSize(CubeState.TOOL_SLOTS, ItemStack.EMPTY);
        var types = new net.minecraft.world.item.Item[]{Items.IRON_PICKAXE, Items.IRON_AXE, Items.IRON_SHOVEL, Items.IRON_SWORD, Items.IRON_HOE};
        for (int slot = 0; slot < CubeState.TOOL_SLOTS; slot++) {
            var tool = new ItemStack(types[slot % 5]);
            tool.setDamageValue(tool.getMaxDamage() - 1);
            tool.set(DataComponents.CUSTOM_NAME, Component.literal("Iron original " + slot));
            expected.set(slot, tool.copy());
            check(CubeInventory.insert(cube, tool) && tool.isEmpty(), "Full cube fixture rejected original " + slot);
        }
        var identity = CubeInventory.state(cube).identity();
        player.setItemSlot(EquipmentSlot.MAINHAND, CubeInventory.select(cube, 0));
        var position = player.blockPosition().below();
        for (int slot : new int[]{0, 5, 1, 6, 2, 7, 3, 8, 4, 9}) {
            check(CubeInventory.state(player.getMainHandItem()).active() == slot, "Break fallback chose wrong original before " + slot);
            if (callbackDamage) player.getMainHandItem().hurtAndBreak(1, player.level(), player,
                    broken -> player.onEquippedItemBroken(broken, EquipmentSlot.MAINHAND));
            else {
                player.level().setBlockAndUpdate(position, Blocks.STONE.defaultBlockState());
                check(player.gameMode.destroyBlock(position), "Native survival mining did not destroy the fixture block");
            }
            expected.set(slot, ItemStack.EMPTY);
            var held = player.getMainHandItem();
            check(CubeInventory.isCube(held), "Native mining destroyed the entire cube after original " + slot);
            var state = CubeInventory.state(held);
            check(state.identity().equals(identity), "Break fallback replaced the original cube identity");
            var actual = state.equipment(held);
            check(actual.get(CubeState.SHELL_SLOT).is(BetterTools.CUBE), "Mining lost the stored shell after original " + slot);
            for (int other = 0; other < CubeState.TOOL_SLOTS; other++)
                check(ItemStack.matches(expected.get(other), actual.get(other)), "Mining changed remaining original " + other + " after " + slot);
        }
        check(player.getMainHandItem().is(BetterTools.CUBE), "Exhausting all tools did not restore the empty shell");
        player.setGameMode(previousMode);
    }

    /** Review: protect player equipment when adding a column, including active legacy reserves and real hoe breakage.
     * Existing pickaxe conservation does not detect shifted indices or a shell mistaken for an editable tool.
     * The fixture writes the old nine-entry layout independently; full component equality is the oracle. */
    private static void hoesAndExistingItems(net.minecraft.server.level.ServerPlayer player) {
        var ops = player.registryAccess().createSerializationContext(JsonOps.INSTANCE);
        var original = new ItemStack(Items.DIAMOND_PICKAXE);
        original.setDamageValue(81);
        original.set(DataComponents.CUSTOM_NAME, Component.literal("Existing reserve"));
        var sword = new ItemStack(Items.IRON_SWORD);
        var oldItems = net.minecraft.core.NonNullList.withSize(9, ItemStack.EMPTY);
        oldItems.set(0, new ItemStack(Items.IRON_PICKAXE));
        oldItems.set(7, sword.copy());
        oldItems.set(8, new ItemStack(BetterTools.CUBE));
        var legacy = CubeState.CODEC.encodeStart(ops, CubeState.empty()).getOrThrow().getAsJsonObject();
        legacy.remove("columns");
        legacy.add("contents", net.minecraft.world.item.component.ItemContainerContents.CODEC.encodeStart(ops,
                net.minecraft.world.item.component.ItemContainerContents.fromItems(oldItems)).getOrThrow());
        legacy.addProperty("active", 4);
        legacy.addProperty("display", 4);
        legacy.addProperty("picked", 7);
        var decoded = CubeState.CODEC.parse(ops, legacy).getOrThrow();
        check(decoded.active() == 5 && decoded.display() == 5 && decoded.picked() == 8, "Existing row selections shifted into hoe slots");
        var held = original.copy();
        held.set(BetterTools.STATE, decoded);
        var restored = CubeInventory.idle(held);
        check(restored.is(BetterTools.CUBE), "Existing transformed item lost its shell");
        var contents = CubeInventory.state(restored).unpack();
        check(ItemStack.isSameItemSameComponents(original, contents.get(5))
                && ItemStack.isSameItemSameComponents(sword, contents.get(8))
                && contents.get(4).isEmpty() && contents.get(9).isEmpty(), "Existing equipment changed or occupied new hoe cells");
        var roundTrip = ItemStack.CODEC.parse(ops, ItemStack.CODEC.encodeStart(ops, restored).getOrThrow()).getOrThrow();
        check(ItemStack.isSameItemSameComponents(restored, roundTrip), "New layout remapped player items twice");

        var hoe = new ItemStack(Items.IRON_HOE);
        hoe.setDamageValue(hoe.getMaxDamage() - 1);
        var reserve = new ItemStack(Items.DIAMOND_HOE);
        reserve.setDamageValue(19);
        var expected = reserve.copy();
        check(CubeInventory.insert(restored, hoe) && CubeInventory.insert(restored, reserve), "Hoe primary/reserve were rejected");
        var rejected = new ItemStack(Items.STONE_HOE);
        check(!CubeInventory.insert(restored, rejected) && rejected.getCount() == 1, "Full hoe column consumed third item");
        check(CubeSelection.block(CubeInventory.state(restored).equipment(restored), Blocks.HAY_BLOCK.defaultBlockState()) == 4,
                "Hoe-effective block did not select the primary hoe");
        player.setItemSlot(EquipmentSlot.MAINHAND, CubeInventory.select(restored, 4));
        player.getMainHandItem().hurtAndBreak(1, player, EquipmentSlot.MAINHAND);
        check(ItemStack.isSameItemSameComponents(expected, CubeInventory.displayedTool(player.getMainHandItem())),
                "Breaking primary hoe lost or damaged reserve");
        var cube = CubeInventory.idle(player.getMainHandItem());
        CubeInventory.pick(cube, 9);
        check(ItemStack.isSameItemSameComponents(expected, CubeInventory.take(cube)), "Last hoe slot could not return original");
        var lastHoe = new ItemStack(Items.IRON_HOE);
        lastHoe.setDamageValue(lastHoe.getMaxDamage() - 1);
        var lastCategory = new ItemStack(BetterTools.CUBE);
        CubeInventory.insert(lastCategory, lastHoe);
        CubeInventory.insert(lastCategory, new ItemStack(Items.IRON_SWORD));
        player.setItemSlot(EquipmentSlot.MAINHAND, CubeInventory.select(lastCategory, 4));
        player.getMainHandItem().hurtAndBreak(1, player, EquipmentSlot.MAINHAND);
        check(player.getMainHandItem().is(Items.IRON_SWORD), "Breaking last tool in a category ignored remaining equipment");
        check(player.getAttributeValue(net.minecraft.world.entity.ai.attributes.Attributes.ATTACK_DAMAGE) == 6,
                "Break fallback did not apply the original sword's native attributes immediately");
    }

    /** Review: extend the conservation case at native menu hooks; a new screen or a lost rejected transfer cannot satisfy it. */
    private static void nativeBundleClicks(net.minecraft.server.level.ServerPlayer player, ItemStack cube, ItemStack expected) {
        var menu = player.inventoryMenu;
        player.getInventory().clearContent();
        player.getInventory().setItem(0, cube);
        menu.setCarried(ItemStack.EMPTY);
        CubeInventory.pick(cube, 5);
        menu.clicked(36, 1, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        check(player.containerMenu == menu, "Cube replaced the original inventory with another menu");
        check(ItemStack.isSameItemSameComponents(expected, menu.getCarried()), "Right-click did not return selected original to cursor");
        menu.clicked(36, 0, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        check(menu.getCarried().isEmpty(), "Cursor equipment was not reinserted");
        menu.clicked(36, 0, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        var heldCube = menu.getCarried();
        check(heldCube.is(BetterTools.CUBE), "Left-click could not pick up whole cube like a bundle");
        menu.clicked(37, 1, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        check(ItemStack.isSameItemSameComponents(expected, player.getInventory().getItem(1)), "Held cube did not right-click original into empty bag slot");
        menu.clicked(37, 0, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        check(player.getInventory().getItem(1).isEmpty(), "Held cube did not collect native slot equipment");
        var before = CubeInventory.state(heldCube);
        var refusing = new net.minecraft.world.inventory.Slot(new net.minecraft.world.SimpleContainer(1), 0, 0, 0) {
            @Override public boolean mayPlace(ItemStack item) { return item.isEmpty(); }
        };
        BetterTools.CUBE.overrideStackedOnOther(heldCube, refusing, net.minecraft.world.inventory.ClickAction.SECONDARY, player);
        check(CubeInventory.state(heldCube).equals(before) && refusing.getItem().isEmpty(), "Rejected target lost or rearranged stored equipment");
        menu.clicked(36, 0, net.minecraft.world.inventory.ContainerInput.PICKUP, player);
        check(menu.getCarried().isEmpty() && player.getInventory().getItem(0).is(BetterTools.CUBE), "Putting down whole cube failed");
    }

    private static void check(boolean condition, String message) {
        if (!condition) throw new AssertionError(message);
    }
}
