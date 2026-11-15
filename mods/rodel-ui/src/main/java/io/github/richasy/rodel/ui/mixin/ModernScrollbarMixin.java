package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractScrollArea;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AbstractScrollArea.class)
public abstract class ModernScrollbarMixin extends AbstractWidget {
    protected ModernScrollbarMixin(int x, int y, int width, int height, Component message) {
        super(x, y, width, height, message);
    }
    @Shadow public abstract int scrollbarWidth();
    @Shadow protected abstract int scrollBarX();
    @Shadow protected abstract int scrollerHeight();
    @Shadow public abstract double scrollAmount();
    @Shadow public abstract int maxScrollAmount();

    @Inject(method = "extractScrollbar", at = @At("HEAD"), cancellable = true)
    private void rodel$scrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY, CallbackInfo callback) {
        var screen = Minecraft.getInstance().gui.screen();
        var context = UiContexts.modern(screen);
        if (context == null || NativeSettingsPage.get(screen) != null) return;
        if (maxScrollAmount() > 0) {
            int x = scrollBarX();
            int top = getY() + (int) (scrollAmount() * (getHeight() - scrollerHeight()) / maxScrollAmount());
            graphics.fill(x, getY(), x + scrollbarWidth(), getY() + getHeight(), (context.theme().edge() & 0x00FFFFFF) | 0x40000000);
            graphics.fill(x, top, x + scrollbarWidth(), top + scrollerHeight(), context.theme().muted());
        }
        callback.cancel();
    }
}
