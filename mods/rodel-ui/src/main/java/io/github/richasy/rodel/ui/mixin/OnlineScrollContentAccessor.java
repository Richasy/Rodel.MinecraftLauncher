package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.layouts.Layout;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(ScrollableLayout.class)
public interface OnlineScrollContentAccessor extends NetworkMenuLayouts.ScrollContent {
    @Override @Accessor("content")
    Layout rodel$content();
}
