package io.github.richasy.rodel.ui.compat;

import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;

/** The vanilla-targeted hooks deliberately have no optional Mod Menu class references. */
public interface ModBrowserPresentation {
    boolean rodel$drawWidget(AbstractWidget widget, GuiGraphicsExtractor graphics, int mouseX, int mouseY);
    boolean rodel$drawInput(AbstractWidget widget, GuiGraphicsExtractor graphics, int x, int y, int width, int height);
    boolean rodel$canvasActive();
    int rodel$canvasWidth();
    int rodel$canvasHeight();
    float rodel$canvasScale();
}
