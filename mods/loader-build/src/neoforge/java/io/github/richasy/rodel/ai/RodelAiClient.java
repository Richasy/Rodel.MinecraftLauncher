package io.github.richasy.rodel.ai;

import net.neoforged.api.distmarker.Dist;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.client.event.RegisterClientCommandsEvent;
import net.neoforged.neoforge.client.event.ScreenEvent;

/** NeoForge binds lifecycle events to the same client behavior and Core conversation. */
@Mod(value = "rodel_ai", dist = Dist.CLIENT)
public final class RodelAiClient {
    static final AiController CONTROLLER = AiClientHooks.CONTROLLER;
    public RodelAiClient(net.neoforged.fml.ModContainer container) {
        container.registerExtensionPoint(net.neoforged.neoforge.client.gui.IConfigScreenFactory.class, (mod, parent) -> new AiClientConfig(parent));
        NeoForge.EVENT_BUS.addListener((RegisterClientCommandsEvent event) -> AiClientHooks.register(event.getDispatcher()));
        NeoForge.EVENT_BUS.addListener((ClientTickEvent.Post event) -> AiClientHooks.tick());
        NeoForge.EVENT_BUS.addListener((ScreenEvent.Init.Post event) -> AiClientHooks.afterInit(event.getScreen(), event::addListener));
    }
}
