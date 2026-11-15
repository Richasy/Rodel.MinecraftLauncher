package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.WidgetTooltipHolder;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(AbstractWidget.class)
public interface WidgetTooltipAccessor {
    @Accessor("tooltip")
    WidgetTooltipHolder rodel$tooltip();
}
