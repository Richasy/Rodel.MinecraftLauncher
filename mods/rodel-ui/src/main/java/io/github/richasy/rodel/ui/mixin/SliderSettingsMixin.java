package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ThemedSlider;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSliderButton;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AbstractSliderButton.class)
public abstract class SliderSettingsMixin extends AbstractWidget implements ThemedSlider {
    @Shadow protected double value;
    @Unique private MenuContext rodel$context;

    protected SliderSettingsMixin(int x, int y, int width, int height, Component message) {
        super(x, y, width, height, message);
    }

    @Override
    public void rodel$setStyle(MenuContext context) {
        rodel$context = context;
    }

    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$slider(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                              float partialTick, CallbackInfo callback) {
        var menu = io.github.richasy.rodel.ui.menus.NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (menu != null && menu.rendering()) rodel$context = menu;
        if (rodel$context == null) return;
        MenuTheme theme = rodel$context.theme();
        float scale = rodel$context.scale();
        graphics.pose().pushMatrix();
        graphics.pose().translate(getX(), getY());
        graphics.pose().scale(scale);
        int width = Math.round(getWidth() / scale);
        int height = Math.round(getHeight() / scale);
        int handleWidth = Math.round(8 / scale);
        float nativeCenter = ((getWidth() - 8) * (float) value + 4) / scale;
        int handleX = Math.max(0, Math.min(width - handleWidth, Math.round(nativeCenter - handleWidth / 2f)));
        int trackY = height / 2 - 3;
        graphics.fill(handleWidth / 2, trackY, width - handleWidth / 2, trackY + 6, theme.edge());
        graphics.fill(handleWidth / 2, trackY, handleX + handleWidth / 2, trackY + 6, active ? theme.primary() : theme.muted());
        theme.panel(graphics, handleX, height / 2 - 12, handleWidth, 24);
        if (isFocused() && rodel$context.focusVisible()) graphics.outline(-2, -2, width + 4, height + 4, theme.focus());
        graphics.pose().popMatrix();
        handleCursor(graphics);
        callback.cancel();
    }
}
