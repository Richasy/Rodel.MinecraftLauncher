package dev.richasy.worldreroll;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.screen.v1.ScreenEvents;
import net.fabricmc.fabric.api.client.screen.v1.Screens;

public final class WorldRerollClient implements ClientModInitializer {
    @Override
    public void onInitializeClient() {
        ScreenEvents.AFTER_INIT.register((client, screen, width, height) -> WorldRerollHooks.afterInit(screen, screen.children(), widget -> Screens.getWidgets(screen).add(widget)));
        ClientTickEvents.END_CLIENT_TICK.register(client -> WorldRerollHooks.tick());
    }
}
