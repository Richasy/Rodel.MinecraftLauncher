package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.tabs.Tab;
import net.minecraft.client.gui.components.tabs.TabManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(TabManager.class)
public abstract class WorldTabManagerMixin {
    @Inject(method = "setCurrentTab(Lnet/minecraft/client/gui/components/tabs/Tab;ZZ)V", at = @At("RETURN"))
    private void rodel$selected(Tab tab, boolean sound, boolean addWidgets, CallbackInfo callback) {
        var screen = Minecraft.getInstance().gui.screen();
        var page = WorldPage.get(screen);
        if (page != null && screen instanceof WorldCreateAccess access && access.rodel$tabManager() == (Object) this) page.refreshTab();
    }
}
