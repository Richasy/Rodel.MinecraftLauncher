package dev.richasy.worldreroll;

import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.event.TickEvent;
import net.minecraftforge.client.event.ScreenEvent;

/** Forge lifecycle adapter; creation, source preservation and LAN guards stay in the shared module. */
@Mod("worldreroll")
public final class WorldRerollClient {
    public WorldRerollClient(net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext context) {
        context.registerExtensionPoint(net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory.class,
            () -> new net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory(RerollClientConfig::new));
        ScreenEvent.Init.Post.BUS.addListener(event -> WorldRerollHooks.afterInit(event.getScreen(), event.getScreen().children(), event::addListener));
        TickEvent.ClientTickEvent.Post.BUS.addListener(event -> WorldRerollHooks.tick());
    }
}
