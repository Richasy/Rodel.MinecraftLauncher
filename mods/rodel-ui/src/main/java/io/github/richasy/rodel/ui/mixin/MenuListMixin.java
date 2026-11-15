package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractSelectionList.class)
public abstract class MenuListMixin extends AbstractWidget {
    protected MenuListMixin(int x, int y, int width, int height, Component message) {
        super(x, y, width, height, message);
    }

    @Inject(method = {"extractListBackground", "extractListSeparators"}, at = @At("HEAD"), cancellable = true)
    private void rodel$background(CallbackInfo callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null) callback.cancel();
    }

    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rowWidth(CallbackInfoReturnable<Integer> callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null)
            callback.setReturnValue(Math.max(32, getWidth() - 20));
    }

    @Inject(method = "extractSelection", at = @At("HEAD"), cancellable = true)
    private void rodel$selection(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry, int color, CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) return;
        graphics.fill(entry.getX(), entry.getY(), entry.getX() + entry.getWidth(), entry.getY() + entry.getHeight(), page.theme().selection());
        graphics.outline(entry.getX(), entry.getY(), entry.getWidth(), entry.getHeight(), page.theme().edge());
        if (isFocused() && page.focusVisible())
            graphics.outline(entry.getX(), entry.getY(), entry.getWidth(), entry.getHeight(), page.theme().focus());
        callback.cancel();
    }
}
