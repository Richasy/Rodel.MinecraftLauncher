package dev.richasy.worldreroll;

import java.util.*;
import java.util.function.Consumer;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.PauseScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;

/** Shared native menu behavior; this independent mod does not depend on Rodel UI or a loader-specific widget list. */
public final class WorldRerollHooks {
    private WorldRerollHooks() { }
    public static void afterInit(Screen screen, List<? extends GuiEventListener> children, Consumer<AbstractWidget> add) {
        Minecraft client = Minecraft.getInstance();
        if (!(screen instanceof PauseScreen) || !RerollController.available(client)) return;
        var anchor = children.stream().filter(Button.class::isInstance).map(Button.class::cast)
            .max(Comparator.comparingInt(Button::getY)).orElse(null);
        if (anchor == null) return;
        int x = anchor.getX() + anchor.getWidth() + 4;
        if (x + 52 > screen.width) x = Math.max(0, anchor.getX() - 56);
        Button button = Button.builder(Component.translatable("worldreroll.short_button"), ignored -> {
            if (RerollController.available(client)) client.gui.setScreen(new RerollSettingsScreen(screen));
        }).bounds(x, anchor.getY(), 52, 20).build();
        button.setTooltip(Tooltip.create(Component.translatable("worldreroll.tooltip"))); add.accept(button);
    }
    public static void tick() {
        Minecraft client = Minecraft.getInstance(); RerollController.tick(client); CreationSpawn.tick(client);
    }
}
