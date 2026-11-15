package io.github.richasy.bettertools;

import net.minecraft.core.registries.Registries;
import net.minecraft.network.protocol.common.custom.CustomPacketPayload;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.CreativeModeTabs;
import net.minecraftforge.event.BuildCreativeModeTabContentsEvent;
import net.minecraftforge.event.TickEvent;
import net.minecraftforge.event.entity.player.PlayerEvent;
import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.fml.event.lifecycle.FMLClientSetupEvent;
import net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext;
import net.minecraftforge.network.Channel;
import net.minecraftforge.network.ChannelBuilder;
import net.minecraftforge.network.NetworkProtocol;
import net.minecraftforge.registries.RegisterEvent;

/** Forge's typed lifecycle and native payload channel; item and permission behavior remains in common code. */
@Mod(BetterTools.ID)
public final class BetterToolsForge {
    static final Channel<CustomPacketPayload> CHANNEL = ChannelBuilder.named(BetterTools.id("main"))
            .networkProtocolVersion(1).payloadChannel().protocol(NetworkProtocol.PLAY).serverbound()
            .addMain(EditingPayload.TYPE, EditingPayload.CODEC, (payload, context) -> {
                BetterTools.editing(context.getSender(), payload.editing());
                context.setPacketHandled(true);
            }).addMain(SelectionPayload.TYPE, SelectionPayload.CODEC, (payload, context) -> {
                payload.apply(context.getSender());
                context.setPacketHandled(true);
            }).addMain(SwapPayload.TYPE, SwapPayload.CODEC, (payload, context) -> {
                payload.apply(context.getSender());
                context.setPacketHandled(true);
            }).build();

    public BetterToolsForge(FMLJavaModLoadingContext context) {
        var group = context.getModBusGroup();
        RegisterEvent.getBus(group).addListener(event -> {
            event.register(Registries.DATA_COMPONENT_TYPE, BetterTools.id("cube"), BetterTools::createState);
            event.register(Registries.ITEM, BetterTools.id("tool_cube"), BetterTools::createCube);
        });
        FMLClientSetupEvent.getBus(group).addListener(event -> event.enqueueWork(() -> BetterToolsForgeClient.initialize(context)));
        BuildCreativeModeTabContentsEvent.BUS.addListener(event -> {
            if (event.getTabKey().equals(CreativeModeTabs.TOOLS_AND_UTILITIES)) event.accept(BetterTools.CUBE);
        });
        TickEvent.ServerTickEvent.Post.BUS.addListener(event -> BetterTools.tick(event.server()));
        PlayerEvent.PlayerLoggedOutEvent.BUS.addListener(event -> {
            if (event.getEntity() instanceof ServerPlayer player) BetterTools.editing(player, false);
        });
    }
}
