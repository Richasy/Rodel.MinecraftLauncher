package io.github.richasy.rodel.ui;

import net.minecraft.client.gui.GuiGraphicsExtractor;

public interface UiBackdrop {
    void rodel$backdrop(GuiGraphicsExtractor graphics, MenuTheme theme, int width, int height, float partialTick);
    void rodel$worldBackdrop(GuiGraphicsExtractor graphics, MenuTheme theme, int width, int height);
}
