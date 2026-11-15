package io.github.richasy.bettertools;

import net.minecraft.network.RegistryFriendlyByteBuf;
import net.minecraft.network.codec.ByteBufCodecs;
import net.minecraft.network.codec.StreamCodec;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;

/** Only suspends the sending player's automatic morphing while their inventory or another screen is open. */
public record EditingPayload(boolean editing) implements CustomPacketPayload {
    public static final Type<EditingPayload> TYPE = new Type<>(BetterTools.id("editing"));
    public static final StreamCodec<RegistryFriendlyByteBuf, EditingPayload> CODEC = StreamCodec.composite(
            ByteBufCodecs.BOOL, EditingPayload::editing, EditingPayload::new);
    @Override public Type<EditingPayload> type() { return TYPE; }
}
