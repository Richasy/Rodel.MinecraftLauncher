package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.ScrollableLayout;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(targets = "com.mojang.realmsclient.gui.screens.configuration.RealmsSettingsTab")
public interface RealmsSettingsAccessMixin extends NetworkMenuLayouts.RealmSettings {
    @Override @Accessor("scrollableLayout") ScrollableLayout rodel$scroll();
}
