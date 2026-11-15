package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.options.OptionsSubScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(OptionsSubScreen.class)
public abstract class OptionsSubScreenSettingsMixin extends Screen {
    @Shadow @Final protected Screen lastScreen;
    @Shadow protected OptionsList list;

    protected OptionsSubScreenSettingsMixin(Component title) {
        super(title);
    }

    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$nativeOptions(CallbackInfo callback) {
        NativeSettingsPage.subpage(this, lastScreen, list);
    }

    @Inject(method = "onClose", at = @At("HEAD"))
    private void rodel$rememberNavigation(CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(this);
        if (page != null) page.rememberSidebar();
    }
}
