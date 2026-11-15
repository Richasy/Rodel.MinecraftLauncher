package io.github.richasy.bettertools;

import java.util.Collections;
import java.util.Map;
import java.util.UUID;
import java.util.WeakHashMap;
import net.minecraft.world.entity.LivingEntity;
import net.minecraft.world.entity.player.Player;

/** Per-player, per-cube combat intent; no saved data or change to vanilla attack reach. */
public final class CubeCombat {
    private static final Map<Player, Engagement> ENGAGEMENTS = Collections.synchronizedMap(new WeakHashMap<>());
    private record Engagement(UUID cube, long expires, int target, UUID targetIdentity, boolean targetDied, boolean awaitingSwing) { }
    private CubeCombat() { }

    /** Ends combat on item change/death, a known target's death, or 100 ticks without another attack/hit. */
    public static boolean active(Player player) {
        var engagement = ENGAGEMENTS.get(player);
        if (engagement == null) return false;
        var state = player.getMainHandItem().get(BetterTools.STATE);
        var target = player.level().getEntity(engagement.target());
        if (!player.isAlive() || state == null || !state.identity().equals(engagement.cube())
                || player.level().getGameTime() >= engagement.expires()) {
            clear(player);
            return false;
        }
        if (engagement.targetDied()) return false;
        if (target instanceof LivingEntity living && living.getUUID().equals(engagement.targetIdentity()) && !living.isAlive()) {
            // The server receives the finishing attack before its native swing packet. Retain one
            // ended marker so that trailing swing cannot start a fresh five-second combat hold.
            if (player instanceof net.minecraft.server.level.ServerPlayer && engagement.awaitingSwing())
                ENGAGEMENTS.put(player, new Engagement(engagement.cube(), engagement.expires(), engagement.target(), engagement.targetIdentity(), true, true));
            else clear(player);
            return false;
        }
        return true;
    }

    /** Records an actual attack or incoming entity hit and equips the best weapon before native damage. */
    public static void engage(Player player, LivingEntity target) {
        engage(player, target, false);
    }

    /** Marks the native attack/swing packet pair, so its finishing swing cannot reopen a completed fight. */
    public static void attack(Player player, LivingEntity target) {
        engage(player, target, true);
    }

    private static void engage(Player player, LivingEntity target, boolean awaitingSwing) {
        var held = player.getMainHandItem();
        if (!CubeInventory.isCube(held) || !player.isAlive() || player.isSpectator()
                || player.containerMenu != player.inventoryMenu
                || player.isUsingItem() && player.getUsedItemHand() == net.minecraft.world.InteractionHand.MAIN_HAND) return;
        var state = CubeInventory.state(held);
        var previous = active(player) ? ENGAGEMENTS.get(player) : null;
        ENGAGEMENTS.put(player, new Engagement(state.identity(), player.level().getGameTime() + 100,
                target != null ? target.getId() : previous == null ? -1 : previous.target(),
                target != null ? target.getUUID() : previous == null ? null : previous.targetIdentity(), false, awaitingSwing));
        CubeSelection.equip(player, CubeSelection.weapon(state.equipment(held)));
    }

    /** A sword swing counts even when it misses; mining swings with another tool do not start combat. */
    public static void swing(Player player) {
        var held = player.getMainHandItem();
        if (!CubeInventory.isCube(held)) return;
        boolean active = active(player);
        var engagement = ENGAGEMENTS.get(player);
        if (engagement != null && engagement.targetDied()) { clear(player); return; }
        var original = held.copy();
        original.remove(BetterTools.STATE);
        if (ToolKind.of(original) == ToolKind.SWORD || active) engage(player, CubeSelection.aimedEntity(player));
    }

    /** Drops transient intent when this player no longer holds a usable cube. */
    public static void clear(Player player) { ENGAGEMENTS.remove(player); }
}
