package io.github.richasy.bettertools.checks;

import io.github.richasy.bettertools.BetterTools;
import io.github.richasy.bettertools.CubeInventory;
import io.github.richasy.bettertools.CubeSelection;

import net.fabricmc.api.ClientModInitializer;
import net.fabricmc.fabric.api.client.event.lifecycle.v1.ClientTickEvents;

/** Test-only entry for normal Fabric/Quilt instances; the production JAR is loaded as an ordinary mod. */
public final class BetterToolsQuiltCheck implements ClientModInitializer {
    @Override public void onInitializeClient() {
        ClientTickEvents.END_CLIENT_TICK.register(client -> NativeClientCheck.tick());
    }
}
