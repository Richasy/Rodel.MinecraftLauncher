package io.github.richasy.rodel.ui.settings;

import io.github.richasy.rodel.ui.MenuContext;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;

public interface NativeFormContext extends MenuContext {
    int p(float value);
    int controlTextSize();
    Component controlText(AbstractWidget widget);
    Component fitValue(AbstractWidget widget, Component full, float availableInFontUnits);
    Component fitLabel(Component full, float availableInFontUnits, boolean tooltip);
    void contextualTooltip(Component full);
    void openChoices(AbstractWidget widget);
    void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                   int thumbHeight, double scroll, int maxScroll);
}
