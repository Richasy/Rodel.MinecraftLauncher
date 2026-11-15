package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.SocialMenuPage;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.social.SocialInteractionsPlayerList;
import net.minecraft.client.gui.screens.social.SocialInteractionsScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(SocialInteractionsScreen.class)
public abstract class SocialMenuLayoutMixin extends Screen implements SocialMenuPage.Access {
    protected SocialMenuLayoutMixin(Component title) { super(title); }
    @Shadow private SocialInteractionsPlayerList socialInteractionsPlayerList;
    @Shadow private EditBox searchBox;
    @Shadow private Button allButton;
    @Shadow private Button hiddenButton;
    @Shadow private Button blockedButton;
    @Shadow private Button blockingHintButton;
    @Unique private int rodel$mouseX;
    @Unique private int rodel$mouseY;

    @Override public SocialInteractionsPlayerList rodel$players() { return socialInteractionsPlayerList; }
    @Override public EditBox rodel$search() { return searchBox; }
    @Override public Button rodel$all() { return allButton; }
    @Override public Button rodel$hidden() { return hiddenButton; }
    @Override public Button rodel$blocked() { return blockedButton; }
    @Override public Button rodel$blockingHint() { return blockingHintButton; }

    @Override public void rodel$replaceSearch(NativeTextBox adapter) {
        boolean focused = getFocused() == searchBox || searchBox.isFocused();
        for (var child : java.util.List.copyOf(children()))
            if (child == searchBox || child instanceof NativeTextBox box && box.editor() == searchBox) removeWidget(child);
        addRenderableWidget(adapter);
        if (focused) setFocused(adapter);
    }

    @Inject(method = {"showPage", "onAddPlayer", "onRemovePlayer"}, at = @At("RETURN"))
    private void rodel$changed(CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof SocialMenuPage page) page.initialized();
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"))
    private void rodel$pointer(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick, CallbackInfo callback) {
        rodel$mouseX = mouseX;
        rodel$mouseY = mouseY;
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$context(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof SocialMenuPage page)
            page.context(graphics, text, rodel$mouseX, rodel$mouseY);
        else graphics.text(font, text, x, y, color);
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;centeredText(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$empty(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        if (NativeMenuPage.get((Screen) (Object) this) instanceof SocialMenuPage page) page.empty(graphics, text);
        else graphics.centeredText(font, text, x, y, color);
    }
}
