package io.github.richasy.bettertools;

import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.server.level.ServerPlayer;

/** Bundle-style wheel selection, scoped to the sending player's current native container and editable slot. */
public record SelectionPayload(int container, int slot, int picked) implements CustomPacketPayload {
    public static final Type<SelectionPayload> TYPE = new Type<>(BetterTools.id("selection"));
    public static final StreamCodec<RegistryFriendlyByteBuf, SelectionPayload> CODEC = StreamCodec.composite(
            ByteBufCodecs.VAR_INT, SelectionPayload::container, ByteBufCodecs.VAR_INT, SelectionPayload::slot,
            ByteBufCodecs.VAR_INT, SelectionPayload::picked, SelectionPayload::new);
    @Override public Type<SelectionPayload> type() { return TYPE; }

    /** Ignores stale container IDs, invalid cells and slots the player cannot edit. */
    public void apply(ServerPlayer player) {
        var menu = player.containerMenu;
        if (menu.containerId != container || slot < 0 || slot >= menu.slots.size() || picked < -1 || picked >= CubeState.TOOL_SLOTS) return;
        var target = menu.getSlot(slot);
        if (!target.allowModification(player) || !CubeInventory.isCube(target.getItem())) return;
        CubeInventory.pick(target.getItem(), picked);
        target.setChanged();
        menu.broadcastChanges();
    }
}
