package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(Tooltip.class)
public interface TooltipTextAccessor {
    @Accessor("message")
    Component rodel$message();
}
