package io.github.richasy.rodel.ui.mixin;

import com.mojang.blaze3d.platform.InputConstants;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.controls.NativeButtonRenderer;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractButton;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.input.KeyEvent;
import net.minecraft.world.entity.HumanoidArm;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractButton.class)
public abstract class AbstractButtonMixin extends AbstractWidget implements ThemedButton {
    @Unique private MenuContext rodel$menu;
    @Unique private MenuIcon rodel$icon;
    @Unique private boolean rodel$primary;
    @Unique private boolean rodel$iconOnly;
    @Unique private boolean rodel$side;

    protected AbstractButtonMixin(int x, int y, int width, int height, Component message) {
        super(x, y, width, height, message);
    }

    @Override
    public void rodel$setStyle(MenuContext menu, MenuIcon icon, boolean primary, boolean iconOnly, boolean side) {
        rodel$menu = menu;
        rodel$icon = icon;
        rodel$primary = primary;
        rodel$iconOnly = iconOnly;
        rodel$side = side;
    }

    @Override
    public boolean rodel$iconOnly() {
        return rodel$menu != null ? rodel$iconOnly
            : io.github.richasy.rodel.ui.menus.NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null
                && io.github.richasy.rodel.ui.menus.NativeMenuPage.iconOnly(this);
    }

    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                              float partialTick, CallbackInfo callback) {
        var nativeMenu = io.github.richasy.rodel.ui.menus.NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (nativeMenu instanceof io.github.richasy.rodel.ui.menus.SocialMenuPage social
            && nativeMenu.rendering() && social.drawTab(this, graphics, mouseX, mouseY)) {
            handleCursor(graphics);
            callback.cancel();
            return;
        }
        if (nativeMenu != null && nativeMenu.rendering()
            && (Object) this instanceof io.github.richasy.rodel.ui.menus.MenuButtonIcon button
            && !getClass().getName().equals("com.mojang.realmsclient.gui.RealmsHeader$NotificationButton")) {
            if (button.rodel$loading()) return;
            if (button.rodel$menuIcon() != null && (rodel$menu != nativeMenu || rodel$icon != button.rodel$menuIcon()
                || rodel$iconOnly != (getWidth() <= 24)))
                rodel$setStyle(nativeMenu, button.rodel$menuIcon(), rodel$menu == nativeMenu && rodel$primary, getWidth() <= 24, false);
        }
        if (nativeMenu != null && nativeMenu.rendering() && rodel$menu != nativeMenu
            && io.github.richasy.rodel.ui.menus.NativeMenuPage.plainButton(this))
            rodel$setStyle(nativeMenu, null, false, false, false);
        if (rodel$menu == null) {
            return;
        }
        if (rodel$menu instanceof NativeSettingsPage page && page.isMainHand(this)) {
            page.hands(graphics, this, mouseX, mouseY);
            handleCursor(graphics);
            callback.cancel();
            return;
        }
        NativeButtonRenderer.draw(graphics, this, rodel$menu, rodel$icon, rodel$primary, rodel$iconOnly, rodel$side);
        handleCursor(graphics);
        callback.cancel();
    }

    @Inject(method = "onClick", at = @At("HEAD"), cancellable = true)
    private void rodel$handClick(MouseButtonEvent event, boolean doubleClick, CallbackInfo callback) {
        if (rodel$menu instanceof NativeSettingsPage page && page.isMainHand(this)) {
            page.handClick(event.x());
            callback.cancel();
        }
    }

    @Inject(method = "keyPressed", at = @At("HEAD"), cancellable = true)
    private void rodel$handKey(KeyEvent event, CallbackInfoReturnable<Boolean> callback) {
        if (rodel$menu instanceof NativeSettingsPage page && page.isMainHand(this)) {
            if (event.key() == InputConstants.KEY_LEFT || event.key() == InputConstants.KEY_RIGHT) {
                page.selectHand(event.key() == InputConstants.KEY_LEFT ? HumanoidArm.LEFT : HumanoidArm.RIGHT);
                callback.setReturnValue(true);
            }
        }
    }
}
