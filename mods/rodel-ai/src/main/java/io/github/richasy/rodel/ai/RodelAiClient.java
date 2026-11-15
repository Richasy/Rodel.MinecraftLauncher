package io.github.richasy.rodel.ai;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.screen.v1.ScreenEvents;

/** Installs client-only command and pause-menu entry points. No AI message is sent to a game server. */
public final class RodelAiClient implements ClientModInitializer {
    static final AiController CONTROLLER = AiClientHooks.CONTROLLER;

    @Override
    public void onInitializeClient() {
        ClientCommandRegistrationCallback.EVENT.register((dispatcher, access) -> AiClientHooks.register(dispatcher));
        ClientTickEvents.END_CLIENT_TICK.register(client -> AiClientHooks.tick());
        ScreenEvents.AFTER_INIT.register((client, screen, width, height) -> AiClientHooks.afterInit(screen, widget -> net.fabricmc.fabric.api.client.screen.v1.Screens.getWidgets(screen).add(widget)));
    }
}
