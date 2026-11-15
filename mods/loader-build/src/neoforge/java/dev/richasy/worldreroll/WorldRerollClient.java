package dev.richasy.worldreroll;

import net.neoforged.api.distmarker.Dist;
import net.neoforged.fml.common.Mod;
import net.neoforged.neoforge.common.NeoForge;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.client.event.ScreenEvent;

/** NeoForge lifecycle adapter for the independent World Reroll module. */
@Mod(value = "worldreroll", dist = Dist.CLIENT)
public final class WorldRerollClient {
    public WorldRerollClient(net.neoforged.fml.ModContainer container) {
        container.registerExtensionPoint(net.neoforged.neoforge.client.gui.IConfigScreenFactory.class, (mod, parent) -> new RerollClientConfig(parent));
        NeoForge.EVENT_BUS.addListener((ScreenEvent.Init.Post event) -> WorldRerollHooks.afterInit(event.getScreen(), event.getScreen().children(), event::addListener));
        NeoForge.EVENT_BUS.addListener((ClientTickEvent.Post event) -> WorldRerollHooks.tick());
    }
}
