package io.github.richasy.rodel.ai;

import com.mojang.brigadier.CommandDispatcher;
import com.mojang.brigadier.arguments.StringArgumentType;
import com.mojang.brigadier.builder.LiteralArgumentBuilder;
import com.mojang.brigadier.builder.RequiredArgumentBuilder;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Shared client behavior; loaders only bind their command, tick and screen lifecycle events. */
public final class AiClientHooks {
    static final AiController CONTROLLER = new AiController();
    private AiClientHooks() { }

    public static <S> void register(CommandDispatcher<S> dispatcher) {
        dispatcher.register(LiteralArgumentBuilder.<S>literal("rai")
            .executes(command -> { CONTROLLER.open(null); return 1; })
            .then(RequiredArgumentBuilder.<S, String>argument("message", StringArgumentType.greedyString())
                .executes(command -> { CONTROLLER.quick(StringArgumentType.getString(command, "message")); return 1; })));
        dispatcher.register(LiteralArgumentBuilder.<S>literal("rai_decide")
            .then(RequiredArgumentBuilder.<S, String>argument("decision", StringArgumentType.greedyString())
                .executes(command -> { CONTROLLER.quickDecision(StringArgumentType.getString(command, "decision")); return 1; })));
        dispatcher.register(LiteralArgumentBuilder.<S>literal("rai_new").executes(command -> { CONTROLLER.resetConsole(); return 1; }));
    }

    public static void tick() { CONTROLLER.tick(); }

    public static void afterInit(Screen screen, java.util.function.Consumer<net.minecraft.client.gui.components.AbstractWidget> add) {
        if (!(screen instanceof PauseScreen pause) || !pause.showsPauseMenu()) return;
        add.accept(Button.builder(Component.translatable("rai.title"), button -> CONTROLLER.open(screen))
            .bounds(Math.max(4, screen.width - 114), 8, 110, 20).build());
    }
}
