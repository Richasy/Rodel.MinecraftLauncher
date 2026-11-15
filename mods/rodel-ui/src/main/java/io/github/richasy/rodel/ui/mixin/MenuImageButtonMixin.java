package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.menus.MenuButtonIcon;
import net.minecraft.client.gui.components.ImageButton;
import net.minecraft.client.gui.components.WidgetSprites;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(ImageButton.class)
public abstract class MenuImageButtonMixin implements MenuButtonIcon {
    @Shadow @Final protected WidgetSprites sprites;

    @Override public boolean rodel$loading() { return false; }
    @Override public MenuIcon rodel$menuIcon() { return MenuIcon.nativeSprite(sprites.enabled()); }
}
