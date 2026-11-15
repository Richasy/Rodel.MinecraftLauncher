package io.github.richasy.rodel.ai;

import net.minecraftforge.fml.common.Mod;
import net.minecraftforge.event.TickEvent;
import net.minecraftforge.client.event.RegisterClientCommandsEvent;
import net.minecraftforge.client.event.ScreenEvent;

/** Forge's typed event buses feed the same client hooks as Fabric and NeoForge. */
@Mod("rodel_ai")
public final class RodelAiClient {
    static final AiController CONTROLLER = AiClientHooks.CONTROLLER;
    public RodelAiClient(net.minecraftforge.fml.javafmlmod.FMLJavaModLoadingContext context) {
        context.registerExtensionPoint(net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory.class,
            () -> new net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory(AiClientConfig::new));
        RegisterClientCommandsEvent.BUS.addListener(event -> {
            AiClientHooks.register(event.getDispatcher());
            org.slf4j.LoggerFactory.getLogger("Rodel AI").info("Registered Forge client AI commands");
        });
        TickEvent.ClientTickEvent.Post.BUS.addListener(event -> AiClientHooks.tick());
        ScreenEvent.Init.Post.BUS.addListener(event -> AiClientHooks.afterInit(event.getScreen(), event::addListener));
        org.slf4j.LoggerFactory.getLogger("Rodel AI").info("Initialized Forge client AI lifecycle");
    }
}
