package io.github.richasy.bettertools;

import io.github.richasy.bettertools.client.BetterToolsClient;
import net.minecraft.client.Minecraft;
import net.minecraftforge.event.TickEvent;

/** Forge client-only screen and transport binding, initialized by its own client lifecycle. */
public final class BetterToolsForgeClient {
    private BetterToolsForgeClient() { }

    /** Reuses the same native container screen and inventory suspension as Fabric and NeoForge. */
    public static void initialize(net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext context) {
        context.registerExtensionPoint(net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory.class,
            () -> new net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory(io.github.richasy.bettertools.client.CubeClientConfig::new));
        net.minecraftforge.client.event.RegisterClientCommandsEvent.BUS.addListener(event -> io.github.richasy.bettertools.client.CubeClientConfig.register(event.getDispatcher()));
        BetterToolsClient.initialize(payload -> BetterToolsForge.CHANNEL.send(payload, Minecraft.getInstance().getConnection().getConnection()));
        TickEvent.ClientTickEvent.Post.BUS.addListener(event -> {
            io.github.richasy.bettertools.client.CubeClientConfig.openPendingScreen();
            var client = Minecraft.getInstance();
            BetterToolsClient.tick(client);
        });
    }
}
