package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.FocusableTextWidget;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(OptionsList.HeaderEntry.class)
public abstract class OptionsHeaderSettingsMixin {
    @Shadow @Final private Screen screen;
    @Shadow @Final private FocusableTextWidget widget;

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$actualGroupTitle(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                       boolean hovered, float partialTick, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        if (page != null) {
            page.group(graphics, (OptionsList.HeaderEntry) (Object) this, widget);
            callback.cancel();
        }
    }
}
