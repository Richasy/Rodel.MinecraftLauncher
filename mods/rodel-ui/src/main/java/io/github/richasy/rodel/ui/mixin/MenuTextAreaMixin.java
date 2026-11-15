package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractTextAreaWidget;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AbstractTextAreaWidget.class)
public abstract class MenuTextAreaMixin extends AbstractWidget {
    protected MenuTextAreaMixin(int x, int y, int width, int height, Component message) {
        super(x, y, width, height, message);
    }

    @Inject(method = "extractBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$background(GuiGraphicsExtractor graphics, CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) return;
        graphics.fill(getX(), getY(), getRight(), getBottom(), page.theme().surface());
        graphics.outline(getX(), getY(), getWidth(), getHeight(), page.theme().edge());
        if (isFocused() && page.focusVisible())
            graphics.outline(getX(), getY(), getWidth(), getHeight(), page.theme().focus());
        callback.cancel();
    }

    @Inject(method = "extractBorder", at = @At("HEAD"), cancellable = true)
    private void rodel$border(CallbackInfo callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null) callback.cancel();
    }
}
