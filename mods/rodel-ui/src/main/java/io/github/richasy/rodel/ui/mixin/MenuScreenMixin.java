package io.github.richasy.rodel.ui.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.events.GuiEventListener;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(Screen.class)
public abstract class MenuScreenMixin {
    @Shadow protected abstract <T extends GuiEventListener> T addWidget(T widget);
    @Inject(method = {"init(II)V", "resize"}, at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;repositionElements()V"))
    private void rodel$prepareResize(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) page.prepare();
    }

    @Inject(method = "init(II)V", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/Screen;init()V"))
    private void rodel$prepareInit(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) page.prepare();
    }

    @Inject(method = {"init(II)V", "resize", "rebuildWidgets"}, at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) {
            page.choices().close();
            page.initialized();
            if (!((Screen) (Object) this).children().contains(page.choices())) addWidget(page.choices());
        }
    }

    @Inject(method = "extractRenderStateWithTooltipAndSubtitles", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;extractDeferredElements(IIF)V"))
    private void rodel$nativeOverlay(GuiGraphicsExtractor graphics, int x, int y, float tick, CallbackInfo callback) {
        var page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) page.overlay(graphics, x, y, tick);
    }

    @WrapMethod(method = "extractRenderStateWithTooltipAndSubtitles")
    private void rodel$nativeViewport(GuiGraphicsExtractor graphics, int x, int y, float tick, Operation<Void> original) {
        var page = NativeMenuPage.get((Screen) (Object) this);
        if (page == null) {
            original.call(graphics, x, y, tick);
            return;
        }
        int mouseX = (int) page.coordinate(x);
        int mouseY = (int) page.coordinate(y);
        try {
            page.begin(graphics, mouseX, mouseY, tick);
            original.call(graphics, mouseX, mouseY, tick);
        } finally {
            page.end(graphics, mouseX, mouseY, tick);
        }
    }

    @Inject(method = {"extractMenuBackground(Lnet/minecraft/client/gui/GuiGraphicsExtractor;)V",
        "extractMenuBackground(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIII)V",
        "extractTransparentBackground"}, at = @At("HEAD"), cancellable = true)
    private void rodel$noNativeTexture(CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) != null) callback.cancel();
    }
}
