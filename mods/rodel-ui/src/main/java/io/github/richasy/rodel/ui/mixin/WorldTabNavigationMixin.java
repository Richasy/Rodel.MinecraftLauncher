package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldCreateAccess;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.tabs.TabNavigationBar;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(TabNavigationBar.class)
public abstract class WorldTabNavigationMixin {
    private AbstractWidget rodel$headerTabs() {
        var screen = Minecraft.getInstance().gui.screen();
        if (NativeMenuPage.get(screen) != null) return (AbstractWidget) (Object) this;
        return WorldPage.get(screen) != null && screen instanceof WorldCreateAccess access && access.rodel$tabs() == (Object) this
            ? (AbstractWidget) (Object) this : null;
    }
    @Inject(method = "isMouseOver", at = @At("HEAD"), cancellable = true)
    private void rodel$hit(double x, double y, CallbackInfoReturnable<Boolean> callback) {
        var bar = rodel$headerTabs();
        if (bar != null) callback.setReturnValue(x >= bar.getX() && x < bar.getRight() && y >= bar.getY() && y < bar.getBottom());
    }
    @Inject(method = "getRectangle", at = @At("HEAD"), cancellable = true)
    private void rodel$rectangle(CallbackInfoReturnable<ScreenRectangle> callback) {
        var bar = rodel$headerTabs();
        if (bar != null) callback.setReturnValue(new ScreenRectangle(bar.getX(), bar.getY(), bar.getWidth(), bar.getHeight()));
    }
}
