package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.ThemedButton;
import io.github.richasy.rodel.ui.UiContexts;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.PauseMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AbstractWidget.class)
public abstract class SettingsTooltipMixin {
    @Inject(method = "extractTooltipForNextRenderPass", at = @At("HEAD"), cancellable = true)
    private void rodel$tooltipPolicy(CallbackInfo callback) {
        var screen = Minecraft.getInstance().gui.screen();
        var menu = NativeMenuPage.get(screen);
        // Pause labels use the same icon/overflow policy as the title and settings pages.
        // Other native menus retain their original status and validation tooltips.
        if (menu != null && !(menu instanceof PauseMenuPage)) return;
        if (UiContexts.owned(screen) != null
            && !((Object) this instanceof ThemedButton button && button.rodel$iconOnly())) callback.cancel();
    }
}
