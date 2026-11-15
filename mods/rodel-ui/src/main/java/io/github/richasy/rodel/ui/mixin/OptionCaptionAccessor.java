package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.OptionInstance;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(OptionInstance.class)
public interface OptionCaptionAccessor<T> {
    @Accessor("caption")
    Component rodel$caption();

    @Accessor("tooltip")
    OptionInstance.TooltipSupplier<T> rodel$tooltipSupplier();
}
