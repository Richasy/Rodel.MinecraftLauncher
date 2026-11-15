package io.github.richasy.bettertools;

import io.github.richasy.bettertools.client.BetterToolsClient;
import net.minecraft.client.Minecraft;
import net.minecraft.network.protocol.common.ServerboundCustomPayloadPacket;
import net.neoforged.neoforge.client.event.ClientTickEvent;
import net.neoforged.neoforge.common.NeoForge;

/** Loaded only from NeoForge's client setup, keeping Minecraft client classes off the dedicated server. */
public final class BetterToolsNeoForgeClient {
    private BetterToolsNeoForgeClient() { }

    /** Binds the shared native screen and sends its editing transitions on the registered native payload. */
    public static void initialize(net.neoforged.fml.ModContainer container) {
        container.registerExtensionPoint(net.neoforged.neoforge.client.gui.IConfigScreenFactory.class,
            (mod, parent) -> new io.github.richasy.bettertools.client.CubeClientConfig(parent));
        NeoForge.EVENT_BUS.addListener((net.neoforged.neoforge.client.event.RegisterClientCommandsEvent event) -> io.github.richasy.bettertools.client.CubeClientConfig.register(event.getDispatcher()));
        BetterToolsClient.initialize(payload -> Minecraft.getInstance().getConnection().send(new ServerboundCustomPayloadPacket(payload)));
        NeoForge.EVENT_BUS.addListener((ClientTickEvent.Post event) -> {
            io.github.richasy.bettertools.client.CubeClientConfig.openPendingScreen();
            var client = Minecraft.getInstance();
            BetterToolsClient.tick(client);
        });
    }
}
