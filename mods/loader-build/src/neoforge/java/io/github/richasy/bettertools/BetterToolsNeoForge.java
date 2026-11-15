package io.github.richasy.bettertools;

import net.minecraft.core.registries.Registries;
import net.minecraft.server.level.ServerPlayer;
import net.minecraft.world.item.CreativeModeTabs;
import net.neoforged.bus.api.IEventBus;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.event.BuildCreativeModeTabContentsEvent;
import net.neoforged.neoforge.event.entity.player.PlayerEvent;
import net.neoforged.neoforge.event.tick.ServerTickEvent;
import net.neoforged.neoforge.network.event.RegisterPayloadHandlersEvent;
import net.neoforged.neoforge.registries.RegisterEvent;

/** Native NeoForge registration and server lifecycle for the independent shared tool cube. */
@Mod(BetterTools.ID)
public final class BetterToolsNeoForge {
    public BetterToolsNeoForge(IEventBus bus, net.neoforged.fml.ModContainer container) {
        bus.addListener((RegisterEvent event) -> {
            event.register(Registries.DATA_COMPONENT_TYPE, BetterTools.id("cube"), BetterTools::createState);
            event.register(Registries.ITEM, BetterTools.id("tool_cube"), BetterTools::createCube);
        });
        bus.addListener((RegisterPayloadHandlersEvent event) -> event.registrar("1")
                .playToServer(EditingPayload.TYPE, EditingPayload.CODEC,
                        (payload, context) -> BetterTools.editing((ServerPlayer)context.player(), payload.editing()))
                .playToServer(SelectionPayload.TYPE, SelectionPayload.CODEC, (payload, context) -> payload.apply((ServerPlayer)context.player()))
                .playToServer(SwapPayload.TYPE, SwapPayload.CODEC, (payload, context) -> payload.apply((ServerPlayer)context.player())));
        bus.addListener((BuildCreativeModeTabContentsEvent event) -> {
            if (event.getTabKey().equals(CreativeModeTabs.TOOLS_AND_UTILITIES)) event.accept(BetterTools.CUBE);
        });
        bus.addListener((net.neoforged.fml.event.lifecycle.FMLClientSetupEvent event) ->
                event.enqueueWork(() -> BetterToolsNeoForgeClient.initialize(container)));
        NeoForge.EVENT_BUS.addListener((ServerTickEvent.Post event) -> BetterTools.tick(event.getServer()));
        NeoForge.EVENT_BUS.addListener((PlayerEvent.PlayerLoggedOutEvent event) -> {
            if (event.getEntity() instanceof ServerPlayer player) BetterTools.editing(player, false);
        });
    }
}
