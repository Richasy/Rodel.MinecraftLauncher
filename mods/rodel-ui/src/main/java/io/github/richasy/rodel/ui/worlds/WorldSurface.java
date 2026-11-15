package io.github.richasy.rodel.ui.worlds;

import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.settings.SettingsChoiceList;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;

public interface WorldSurface extends NativeFormContext {
    default WorldMoreNavigation moreNavigation() { return null; }
    static WorldSurface get(Screen screen) {
        return screen instanceof WorldPageHost host ? host.rodel$worldPage() : null;
    }

    void prepare();
    void beginInit();
    void initialized();
    void layout();
    void render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick);
    MouseButtonEvent mouse(MouseButtonEvent event);
    double coordinate(double value);
    SettingsChoiceList choices();
    void text(GuiGraphicsExtractor graphics, Component value, float x, float y, float size, int color, boolean secondary);
}
