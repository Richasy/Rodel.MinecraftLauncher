package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import net.minecraft.client.OptionInstance;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(targets = "net.minecraft.client.OptionInstance$OptionInstanceSliderButton")
public abstract class SliderPresentationMixin implements NativeOptionPresentation {
    @Shadow @Final private OptionInstance<?> instance;

    @Override
    public Component rodel$caption() {
        return ((OptionCaptionAccessor) (Object) instance).rodel$caption();
    }

    @Override
    public Component rodel$valueText() {
        return ((AbstractWidget) (Object) this).getMessage();
    }
}
