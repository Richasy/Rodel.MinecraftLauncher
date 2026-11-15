package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.menus.MenuButtonIcon;
import net.minecraft.client.gui.components.SpriteIconButton;
import net.minecraft.client.gui.components.WidgetSprites;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(SpriteIconButton.class)
public abstract class MenuSpriteButtonMixin implements MenuButtonIcon {
    @Shadow @Final protected WidgetSprites sprite;
    @Shadow private boolean loading;

    @Override public boolean rodel$loading() { return loading; }

    @Override
    public MenuIcon rodel$menuIcon() {
        return MenuIcon.nativeSprite(sprite.enabled());
    }
}
