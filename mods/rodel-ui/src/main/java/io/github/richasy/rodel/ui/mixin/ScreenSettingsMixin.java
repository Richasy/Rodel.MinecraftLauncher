package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.ModernScreen;
import io.github.richasy.rodel.ui.UiBackdrop;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.NativeDialog;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import io.github.richasy.rodel.ui.worlds.WorldPageHost;
import io.github.richasy.rodel.ui.worlds.WorldSurface;
import io.github.richasy.rodel.ui.menus.MenuPageHost;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.events.AbstractContainerEventHandler;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import com.mojang.blaze3d.platform.InputConstants;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(Screen.class)
public abstract class ScreenSettingsMixin extends AbstractContainerEventHandler implements SettingsHost, DialogHost, ModernScreen, UiBackdrop, WorldPageHost, MenuPageHost {
    @Unique private NativeSettingsPage rodel$settings;
    @Unique private MenuContext rodel$rootContext;
    @Unique private Screen rodel$nativeParent;
    @Unique private NativeDialog rodel$dialog;
    @Unique private WorldSurface rodel$world;
    @Unique private NativeMenuPage rodel$menuPage;

    @Override public NativeMenuPage rodel$menuPage() { return rodel$menuPage; }
    @Override public void rodel$menuPage(NativeMenuPage page) { rodel$menuPage = page; }

    @Override public WorldSurface rodel$worldPage() { return rodel$world; }
    @Override public void rodel$worldPage(WorldSurface page) { rodel$world = page; }

    @Override public Screen rodel$nativeParent() { return rodel$nativeParent; }
    @Override public void rodel$nativeParent(Screen parent) { rodel$nativeParent = parent; }
    @Override public NativeDialog rodel$dialog() { return rodel$dialog; }
    @Override public void rodel$dialog(NativeDialog dialog) { rodel$dialog = dialog; }
    @Override public MenuContext rodel$modernContext() { return rodel$menuPage != null ? rodel$menuPage : rodel$world == null ? rodel$dialog : rodel$world; }

    @Override
    public MenuContext rodel$rootContext() { return rodel$rootContext; }

    @Override
    public void rodel$rootContext(MenuContext context) { rodel$rootContext = context; }

    @Shadow protected abstract void extractPanorama(GuiGraphicsExtractor graphics, float partialTick);
    @Shadow protected abstract void extractBlurredBackground(GuiGraphicsExtractor graphics);

    @Override
    public NativeSettingsPage rodel$settingsPage() {
        return rodel$settings;
    }

    @Override
    public void rodel$settingsPage(NativeSettingsPage page) {
        rodel$settings = page;
    }

    @Inject(method = "init(II)V", at = @At("RETURN"))
    private void rodel$activate(int width, int height, CallbackInfo callback) {
        if (rodel$settings != null) rodel$settings.activated();
        if (rodel$dialog != null) rodel$dialog.initialized();
    }

    @Inject(method = "init()V", at = @At("HEAD"))
    private void rodel$baseDialogLayout(CallbackInfo callback) {
        if (rodel$dialog != null) rodel$dialog.prepare();
    }

    @Inject(method = "resize", at = @At("RETURN"))
    private void rodel$resize(int width, int height, CallbackInfo callback) {
        if (rodel$settings != null) rodel$settings.layout();
        if (rodel$dialog != null) rodel$dialog.initialized();
    }

    @Inject(method = "extractBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$background(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                  float partialTick, CallbackInfo callback) {
        if (rodel$settings == null) {
            if (((ModernScreen) this).rodel$modernContext() != null) callback.cancel();
            return;
        }
        if (!rodel$settings.theme().highContrast()) {
            if (net.minecraft.client.Minecraft.getInstance().level == null) extractPanorama(graphics, partialTick);
            extractBlurredBackground(graphics);
        }
        rodel$settings.shade(graphics);
        if (net.minecraft.client.Minecraft.getInstance().level != null)
            net.minecraft.client.Minecraft.getInstance().gui.hud.extractDeferredSubtitles();
        callback.cancel();
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$chrome(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                              float partialTick, CallbackInfo callback) {
        if (rodel$world != null) {
            rodel$world.render(graphics, mouseX, mouseY, partialTick);
            callback.cancel();
            return;
        }
        if (rodel$dialog != null) {
            rodel$dialog.render(graphics, mouseX, mouseY, partialTick);
            callback.cancel();
            return;
        }
        if (rodel$settings != null) rodel$settings.chrome(graphics);
    }

    @Inject(method = "extractRenderState", at = @At("RETURN"))
    private void rodel$choicesOverlay(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                        float partialTick, CallbackInfo callback) {
        if (rodel$settings != null) rodel$settings.overlay(graphics, mouseX, mouseY, partialTick);
    }

    @Override
    public boolean mouseClicked(MouseButtonEvent event, boolean doubleClick) {
        if (rodel$world != null) {
            var mouse = rodel$world.mouse(event);
            return rodel$world.choices().visible ? rodel$world.choices().mouseClicked(mouse, doubleClick) : super.mouseClicked(mouse, doubleClick);
        }
        if (rodel$dialog != null) return super.mouseClicked(rodel$dialog.mouse(event), doubleClick);
        return rodel$settings != null && rodel$settings.choicesOpen()
            ? rodel$settings.choiceClick(event, doubleClick) : super.mouseClicked(event, doubleClick);
    }

    @Override
    public boolean mouseScrolled(double x, double y, double horizontal, double vertical) {
        if (rodel$world != null) return rodel$world.choices().visible
            ? rodel$world.choices().mouseScrolled(rodel$world.coordinate(x), rodel$world.coordinate(y), horizontal, vertical)
            : super.mouseScrolled(rodel$world.coordinate(x), rodel$world.coordinate(y), horizontal, vertical);
        if (rodel$dialog != null) return super.mouseScrolled(rodel$dialog.coordinate(x), rodel$dialog.coordinate(y), horizontal, vertical);
        return rodel$settings != null && rodel$settings.choicesOpen()
            ? rodel$settings.choiceScroll(x, y, horizontal, vertical) : super.mouseScrolled(x, y, horizontal, vertical);
    }

    @Override
    public boolean mouseDragged(MouseButtonEvent event, double x, double y) {
        if (rodel$world != null) return rodel$world.choices().visible
            ? rodel$world.choices().mouseDragged(rodel$world.mouse(event), rodel$world.coordinate(x), rodel$world.coordinate(y))
            : super.mouseDragged(rodel$world.mouse(event), rodel$world.coordinate(x), rodel$world.coordinate(y));
        if (rodel$dialog != null) return super.mouseDragged(rodel$dialog.mouse(event), rodel$dialog.coordinate(x), rodel$dialog.coordinate(y));
        return rodel$settings != null && rodel$settings.choicesOpen()
            ? rodel$settings.choiceDrag(event, x, y) : super.mouseDragged(event, x, y);
    }

    @Override
    public boolean mouseReleased(MouseButtonEvent event) {
        if (rodel$world != null) return rodel$world.choices().visible
            ? rodel$world.choices().mouseReleased(rodel$world.mouse(event)) : super.mouseReleased(rodel$world.mouse(event));
        if (rodel$dialog != null) return super.mouseReleased(rodel$dialog.mouse(event));
        return rodel$settings != null && rodel$settings.choicesOpen()
            ? rodel$settings.choiceRelease(event) : super.mouseReleased(event);
    }

    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void rodel$choiceKey(KeyEvent event, CallbackInfoReturnable<Boolean> callback) {
        if (rodel$world != null && rodel$world.choices().visible) {
            callback.setReturnValue(rodel$world.choices().keyPressed(event));
            return;
        }
        if (rodel$settings == null) return;
        if (rodel$settings.choicesOpen()) callback.setReturnValue(rodel$settings.choiceKey(event));
        else if (event.key() == InputConstants.KEY_ESCAPE) {
            rodel$settings.closeToParent();
            callback.setReturnValue(true);
        }
    }

    @Override
    public void rodel$worldBackdrop(GuiGraphicsExtractor graphics, MenuTheme theme, int width, int height) {
        if (!theme.highContrast()) extractBlurredBackground(graphics);
        graphics.fill(0, 0, width, height, theme.highContrast() ? theme.surface() : theme.light() ? 0x35FFF8E4 : 0x4809161B);
        net.minecraft.client.Minecraft.getInstance().gui.hud.extractDeferredSubtitles();
    }

    @Override
    public void rodel$backdrop(GuiGraphicsExtractor graphics, MenuTheme theme, int width, int height, float partialTick) {
        Screen screen = (Screen) (Object) this;
        if (net.minecraft.client.Minecraft.getInstance().level != null
            && screen.getClass() != net.minecraft.client.gui.screens.GenericMessageScreen.class) {
            rodel$worldBackdrop(graphics, theme, width, height);
            return;
        }
        int savedWidth = screen.width;
        int savedHeight = screen.height;
        screen.width = width;
        screen.height = height;
        try {
            if (!theme.highContrast()) {
                extractPanorama(graphics, partialTick);
                extractBlurredBackground(graphics);
            }
        } finally {
            screen.width = savedWidth;
            screen.height = savedHeight;
        }
        graphics.fill(0, 0, width, height, theme.highContrast() ? theme.surface() : theme.light() ? 0xA8FFF8E4 : 0xB809161B);
    }
}
