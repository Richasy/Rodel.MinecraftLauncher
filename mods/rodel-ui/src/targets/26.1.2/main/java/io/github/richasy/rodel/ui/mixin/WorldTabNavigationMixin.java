package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.mixin.compat.LegacyLayoutAccess;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** The 26.1 navigation bar owns a layout rather than extending AbstractWidget. */
@Mixin(TabNavigationBar.class)
public abstract class WorldTabNavigationMixin {
    @Inject(method = "isMouseOver", at = @At("HEAD"), cancellable = true)
    private void rodel$hit(double x, double y, CallbackInfoReturnable<Boolean> callback) {
        var screen = Minecraft.getInstance().screen;
        if (NativeMenuPage.get(screen) == null && !(WorldPage.get(screen) != null
            && screen instanceof WorldCreateAccess access && access.rodel$tabs() == (Object) this)) return;
        var bounds = ((LegacyLayoutAccess.Bar) this).rodel$layout().getRectangle();
        callback.setReturnValue(x >= bounds.left() && x < bounds.right() && y >= bounds.top() && y < bounds.bottom());
    }
}
