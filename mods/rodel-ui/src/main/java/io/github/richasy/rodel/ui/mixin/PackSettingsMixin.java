package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.PackSettingsHost;
import io.github.richasy.rodel.ui.settings.SettingsHost;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.packs.PackSelectionScreen;
import net.minecraft.client.gui.screens.packs.PackSelectionModel;
import net.minecraft.client.gui.screens.packs.TransferableSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(PackSelectionScreen.class)
public abstract class PackSettingsMixin implements PackSettingsHost {
    @Shadow private TransferableSelectionList availablePackList;
    @Shadow private TransferableSelectionList selectedPackList;
    @Shadow @Final private PackSelectionModel model;
    @Shadow @Final private static Component AVAILABLE_TITLE;
    @Shadow @Final private static Component SELECTED_TITLE;

    @Override public TransferableSelectionList rodel$availablePacks() { return availablePackList; }
    @Override public TransferableSelectionList rodel$selectedPacks() { return selectedPackList; }
    @Override public Component rodel$availableTitle() { return AVAILABLE_TITLE; }
    @Override public Component rodel$selectedTitle() { return SELECTED_TITLE; }
    @Override public int rodel$packRows() {
        return (int) Math.max(model.getUnselected().count(), model.getSelected().count());
    }

    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$packPage(CallbackInfo callback) {
        Screen screen = (Screen) (Object) this;
        Screen parent = ((SettingsHost) screen).rodel$nativeParent();
        if (parent != null) NativeSettingsPage.nativePage(screen, parent);
    }

    @Inject(method = "onClose", at = @At("HEAD"))
    private void rodel$saveNavigation(CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get((Screen) (Object) this);
        if (page != null) page.rememberSidebar();
    }

    @ModifyArg(method = "setInitialFocus", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/packs/PackSelectionScreen;setInitialFocus(Lnet/minecraft/client/gui/components/events/GuiEventListener;)V"), index = 0)
    private GuiEventListener rodel$packSearchFocus(GuiEventListener widget) {
        NativeSettingsPage page = NativeSettingsPage.get((Screen) (Object) this);
        if (page != null) return page.focusTarget(widget);
        var world = WorldSubpage.get((Screen) (Object) this);
        return world == null ? widget : world.focusTarget(widget);
    }
}
