package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.menus.MenuButtonIcon;
import net.minecraft.client.gui.components.LockIconButton;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(LockIconButton.class)
public abstract class MenuLockButtonMixin implements MenuButtonIcon {
    @Shadow public abstract boolean isLocked();
    @Override public boolean rodel$loading() { return false; }
    @Override public MenuIcon rodel$menuIcon() { return isLocked() ? MenuIcon.LOCK : MenuIcon.UNLOCK; }
}
