package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.layouts.HeaderAndFooterLayout;
import net.minecraft.client.gui.layouts.LayoutElement;
import net.minecraft.client.gui.layouts.LinearLayout;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.WorldOptionsScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Adapts the real 26.2 world controls and Done callback to the common scrollable composition. */
@Mixin(WorldOptionsScreen.class)
public abstract class OnlineWorldOptionsLayoutMixin extends Screen implements NetworkMenuLayouts.WorldOptions {
    @Unique private net.minecraft.client.gui.layouts.Layout rodel$content;
    @Unique private ScrollableLayout rodel$scroll;
    @Unique private Button rodel$done;

    protected OnlineWorldOptionsLayoutMixin(Component title) { super(title); }

    @Redirect(method = "init", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/layouts/HeaderAndFooterLayout;addToContents(Lnet/minecraft/client/gui/layouts/LayoutElement;)Lnet/minecraft/client/gui/layouts/LayoutElement;"))
    private LayoutElement rodel$captureContent(HeaderAndFooterLayout layout, LayoutElement content) {
        rodel$content = (net.minecraft.client.gui.layouts.Layout) content;
        return layout.addToContents(content);
    }

    @Redirect(method = "init", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/layouts/HeaderAndFooterLayout;addToFooter(Lnet/minecraft/client/gui/layouts/LayoutElement;)Lnet/minecraft/client/gui/layouts/LayoutElement;"))
    private LayoutElement rodel$captureDone(HeaderAndFooterLayout layout, LayoutElement done) {
        rodel$done = (Button) done;
        return layout.addToFooter(done);
    }

    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$wrapContent(CallbackInfo callback) {
        if (NativeMenuPage.get(this) == null) return;
        LinearLayout column = LinearLayout.vertical();
        column.addChild(rodel$content);
        rodel$content.visitWidgets(this::removeWidget);
        rodel$scroll = new ScrollableLayout(minecraft, column, height);
        rodel$scroll.visitWidgets(this::addRenderableWidget);
        rodel$layout(callback);
    }

    @Override public ScrollableLayout rodel$scroll() { return rodel$scroll; }
    @Override public Button rodel$apply() { return null; }
    @Override public Button rodel$back() { return rodel$done; }

    @Inject(method = {"repositionElements", "onGamemasterPermissionChanged", "onDifficultyChanged"}, at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get(this);
        if (page != null) NetworkMenuLayouts.layout(page);
    }
}
