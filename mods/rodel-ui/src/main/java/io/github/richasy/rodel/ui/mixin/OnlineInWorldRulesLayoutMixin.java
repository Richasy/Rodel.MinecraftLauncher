package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.options.InWorldGameRulesScreen")
public abstract class OnlineInWorldRulesLayoutMixin {
    @Inject(method = {"initContent", "onGameRuleValuesUpdated", "onGamemasterPermissionChanged"},
        at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        Screen screen = (Screen) (Object) this;
        if (!screen.getClass().getName().equals("net.minecraft.client.gui.screens.options.InWorldGameRulesScreen")) return;
        NativeMenuPage page = NativeMenuPage.get(screen);
        if (page != null) NetworkMenuLayouts.layout(page);
    }
}
