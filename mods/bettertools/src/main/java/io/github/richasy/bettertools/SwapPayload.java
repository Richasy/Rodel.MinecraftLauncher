package io.github.richasy.bettertools;

import java.util.UUID;
import net.minecraft.core.UUIDUtil;
import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.server.level.ServerPlayer;

/** Exchanges one selected tool's priority within the sending player's current editable native container. */
public record SwapPayload(int container, int slot, int picked, UUID identity) implements CustomPacketPayload {
    public static final Type<SwapPayload> TYPE = new Type<>(BetterTools.id("swap"));
    public static final StreamCodec<RegistryFriendlyByteBuf, SwapPayload> CODEC = StreamCodec.composite(
            ByteBufCodecs.VAR_INT, SwapPayload::container, ByteBufCodecs.VAR_INT, SwapPayload::slot,
            ByteBufCodecs.VAR_INT, SwapPayload::picked, UUIDUtil.STREAM_CODEC, SwapPayload::identity, SwapPayload::new);
    @Override public Type<SwapPayload> type() { return TYPE; }

    /** Rejects stale selections, replaced cubes, noneditable slots and cursor transfers before changing any originals. */
    public void apply(ServerPlayer player) {
        var menu = player.containerMenu;
        if (menu.containerId != container || slot < 0 || slot >= menu.slots.size()
                || picked < 0 || picked >= CubeState.TOOL_SLOTS || !menu.getCarried().isEmpty()) return;
        var target = menu.getSlot(slot);
        var cube = target.getItem();
        var state = cube.get(BetterTools.STATE);
        if (!target.allowModification(player) || state == null || state.picked() != picked || !state.identity().equals(identity)) return;
        if (!CubeInventory.swapPicked(cube)) return;
        target.setChanged();
        menu.broadcastChanges();
    }
}
