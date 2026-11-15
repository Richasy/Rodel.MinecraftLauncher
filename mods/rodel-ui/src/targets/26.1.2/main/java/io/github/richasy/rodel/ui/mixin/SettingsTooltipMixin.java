package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.PauseMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.WidgetTooltipHolder;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

/** Retains native tooltip state while applying the same owned-page tooltip policy on 26.1. */
@Mixin(AbstractWidget.class)
public abstract class SettingsTooltipMixin {
    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/components/WidgetTooltipHolder;refreshTooltipForNextRenderPass(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIZZLnet/minecraft/client/gui/navigation/ScreenRectangle;)V"))
    private void rodel$tooltip(WidgetTooltipHolder holder, GuiGraphicsExtractor graphics, int x, int y,
                               boolean hovered, boolean focused, ScreenRectangle bounds) {
        var screen = Minecraft.getInstance().screen;
        var menu = NativeMenuPage.get(screen);
        if ((menu == null || menu instanceof PauseMenuPage) && UiContexts.owned(screen) != null
            && !((Object) this instanceof ThemedButton button && button.rodel$iconOnly())) return;
        holder.refreshTooltipForNextRenderPass(graphics, x, y, hovered, focused, bounds);
    }
}
