package io.github.richasy.rodel.ui.worlds;

import java.util.ArrayList;
import java.util.List;
import io.github.richasy.rodel.ui.SidebarNavigation;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.input.InputWithModifiers;

final class WorldMoreSidebar {
    private final WorldSurface surface;
    private final WorldMoreNavigation navigation;
    private final List<Button> buttons = new ArrayList<>();

    WorldMoreSidebar(WorldSurface surface, WorldMoreNavigation navigation) {
        this.surface = surface;
        this.navigation = navigation;
        for (var route : WorldMoreNavigation.Route.values()) buttons.add(new Button(0, 0, 1, 1,
            navigation.label(route), ignored -> {}, narration -> narration.get()) {
                @Override public void onPress(InputWithModifiers input) {
                    if (surface instanceof WorldSubpage page) navigation.select(page, this, route, input);
                }
                @Override protected void extractContents(GuiGraphicsExtractor graphics, int x, int y, float partialTick) {
                    SidebarNavigation.render(graphics, surface, this, navigation.selected() == route,
                        isFocused() && surface.focusVisible());
                }
            });
    }

    List<Button> widgets() { return buttons; }
    void layout(int x, int y, int width) {
        int height = surface.density().control();
        for (int i = 0; i < buttons.size(); i++)
            buttons.get(i).setRectangle(width - surface.p(12), height, x, y + 4 + i * (height + surface.p(8)));
    }
}
