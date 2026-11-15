package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.tabs.TabManager;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.friends.FriendsOverlayScreen;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyConstant;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(FriendsOverlayScreen.class)
public abstract class OnlineFriendsLayoutMixin extends Screen implements NetworkMenuLayouts.Friends {
    @Shadow @Final private TabManager tabManager;
    @Shadow private TabNavigationBar tabNavigationBar;
    @Shadow private LinearLayout layout;

    protected OnlineFriendsLayoutMixin(Component title) { super(title); }

    @Override public TabManager rodel$tabs() { return tabManager; }
    @Override public TabNavigationBar rodel$bar() { return tabNavigationBar; }
    @Override public LinearLayout rodel$layout() { return layout; }

    @ModifyConstant(method = {"init", "getOverlayWidth", "repositionElements"}, constant = @Constant(intValue = 220), require = 0)
    private int rodel$contentWidth(int original) {
        Screen screen = (Screen) (Object) this;
        return NativeMenuPage.get(screen) == null ? original : Math.min(320, screen.width - 56);
    }

    @ModifyConstant(method = "init", constant = @Constant(intValue = 110))
    private int rodel$tabWidth(int original) {
        Screen screen = (Screen) (Object) this;
        return NativeMenuPage.get(screen) == null ? original : Math.min(320, screen.width - 56) / 2;
    }

    @Inject(method = {"init", "repositionElements", "refreshLists"}, at = @At("RETURN"))
    private void rodel$compose(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(this);
        if (page != null && layout != null) NetworkMenuLayouts.layout(page);
    }

    @Inject(method = "mouseClicked", at = @At("HEAD"), cancellable = true)
    private void rodel$headerClick(MouseButtonEvent event, boolean doubleClick, CallbackInfoReturnable<Boolean> callback) {
        if (NetworkMenuLayouts.consumesFriendsHeaderClick(this, event.x(), event.y()))
            callback.setReturnValue(super.mouseClicked(event, doubleClick));
    }
}
