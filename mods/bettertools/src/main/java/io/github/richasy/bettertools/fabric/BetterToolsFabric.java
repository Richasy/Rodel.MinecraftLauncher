package io.github.richasy.bettertools.fabric;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.EditingPayload;
import io.github.richasy.bettertools.SelectionPayload;
import io.github.richasy.bettertools.SwapPayload;
import net.fabricmc.api.ModInitializer;
import net.fabricmc.fabric.api.creativetab.v1.CreativeModeTabEvents;
import net.fabricmc.fabric.api.event.lifecycle.v1.ServerTickEvents;
import net.fabricmc.fabric.api.networking.v1.PayloadTypeRegistry;
import net.fabricmc.fabric.api.networking.v1.ServerPlayConnectionEvents;
import net.fabricmc.fabric.api.networking.v1.ServerPlayNetworking;
import net.minecraft.core.Registry;
import net.minecraft.core.registries.BuiltInRegistries;
import net.minecraft.world.item.CreativeModeTabs;

/** Fabric/Quilt lifecycle and transport only; shared code owns the real inventory and target behavior. */
public final class BetterToolsFabric implements ModInitializer {
    @Override public void onInitialize() {
        Registry.register(BuiltInRegistries.DATA_COMPONENT_TYPE, BetterTools.id("cube"), BetterTools.createState());
        Registry.register(BuiltInRegistries.ITEM, BetterTools.id("tool_cube"), BetterTools.createCube());
        PayloadTypeRegistry.serverboundPlay().register(EditingPayload.TYPE, EditingPayload.CODEC);
        ServerPlayNetworking.registerGlobalReceiver(EditingPayload.TYPE, (payload, context) -> BetterTools.editing(context.player(), payload.editing()));
        PayloadTypeRegistry.serverboundPlay().register(SelectionPayload.TYPE, SelectionPayload.CODEC);
        ServerPlayNetworking.registerGlobalReceiver(SelectionPayload.TYPE, (payload, context) -> payload.apply(context.player()));
        PayloadTypeRegistry.serverboundPlay().register(SwapPayload.TYPE, SwapPayload.CODEC);
        ServerPlayNetworking.registerGlobalReceiver(SwapPayload.TYPE, (payload, context) -> payload.apply(context.player()));
        ServerPlayConnectionEvents.DISCONNECT.register((handler, server) -> BetterTools.editing(handler.player, false));
        ServerTickEvents.END_SERVER_TICK.register(BetterTools::tick);
        CreativeModeTabEvents.modifyOutputEvent(CreativeModeTabs.TOOLS_AND_UTILITIES).register(entries -> entries.accept(BetterTools.CUBE));
    }
}
