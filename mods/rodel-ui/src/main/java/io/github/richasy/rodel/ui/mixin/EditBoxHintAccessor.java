package io.github.richasy.rodel.ui.mixin;

import net.minecraft.client.gui.components.EditBox;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(EditBox.class)
public interface EditBoxHintAccessor {
    @Accessor("hint") Component rodel$hint();
}
