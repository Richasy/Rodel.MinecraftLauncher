package io.github.richasy.bettertools.fabric;

import io.github.richasy.bettertools.EditingPayload;
import io.github.richasy.bettertools.client.BetterToolsClient;
import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;
import net.fabricmc.fabric.api.client.networking.v1.ClientPlayNetworking;

/** Keeps Fabric-specific client events out of the shared screen and item code. */
public final class BetterToolsFabricClient implements ClientModInitializer {
    @Override public void onInitializeClient() {
        net.fabricmc.fabric.api.client.command.v2.ClientCommandRegistrationCallback.EVENT.register((dispatcher, access) -> io.github.richasy.bettertools.client.CubeClientConfig.register(dispatcher));
        BetterToolsClient.initialize(ClientPlayNetworking::send);
        ClientTickEvents.END_CLIENT_TICK.register(client -> {
            io.github.richasy.bettertools.client.CubeClientConfig.openPendingScreen();
            if (ClientPlayNetworking.canSend(EditingPayload.TYPE)) BetterToolsClient.tick(client);
        });
    }
}
