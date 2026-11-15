package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.gui.components.Button;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(Button.class)
public interface ButtonActionAccessor {
    @Accessor("onPress")
    Button.OnPress rodel$action();

    @Mutable
    @Accessor("onPress")
    void rodel$action(Button.OnPress action);
}
