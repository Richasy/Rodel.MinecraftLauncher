package io.github.richasy.rodel.ai;

import com.mojang.brigadier.arguments.StringArgumentType;
import net.minecraft.commands.Commands;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.network.chat.Component;
import net.minecraftforge.api.distmarker.Dist;
import net.minecraftforge.client.event.RegisterClientCommandsEvent;
import net.minecraftforge.client.event.ScreenEvent;
import net.minecraftforge.event.TickEvent;
import net.minecraftforge.eventbus.api.SubscribeEvent;
import net.minecraftforge.fml.common.Mod;

/** Forge lifecycle adapter; conversations and permissions remain in the shared controller and Core. */
@Mod.EventBusSubscriber(modid = "rodel_ai", value = Dist.CLIENT)
public final class RodelAiClient {
    static final AiController CONTROLLER = new AiController();

    /** Registers the local theme page in Forge's mod list. */
    public static void registerConfig() {
        net.minecraftforge.fml.ModLoadingContext.get().registerExtensionPoint(net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory.class,
            () -> new net.minecraftforge.client.ConfigScreenHandler.ConfigScreenFactory((client, parent) -> new AiClientConfig(parent)));
    }

    @SubscribeEvent
    public static void commands(RegisterClientCommandsEvent event) {
        event.getDispatcher().register(Commands.literal("rai")
            .executes(command -> { CONTROLLER.open(Minecraft.getInstance().screen); return 1; })
            .then(Commands.argument("message", StringArgumentType.greedyString())
                .executes(command -> { CONTROLLER.quick(StringArgumentType.getString(command, "message")); return 1; })));
        event.getDispatcher().register(Commands.literal("rai_new").executes(command -> { CONTROLLER.resetConsole(); return 1; }));
        event.getDispatcher().register(Commands.literal("rai_decide").then(Commands.argument("decision", StringArgumentType.greedyString())
            .executes(command -> { CONTROLLER.quickDecision(StringArgumentType.getString(command, "decision")); return 1; })));
    }

    @SubscribeEvent
    public static void tick(TickEvent.ClientTickEvent event) {
        if (event.phase == TickEvent.Phase.END) CONTROLLER.tick();
    }

    @SubscribeEvent
    public static void screen(ScreenEvent.Init.Post event) {
        if (!(event.getScreen() instanceof PauseScreen)) return;
        int width = event.getScreen().width;
        event.addListener(Button.builder(Component.translatable("rai.title"), button -> CONTROLLER.open(event.getScreen()))
            .bounds(width - 110, 8, 100, 20).build());
    }
}
