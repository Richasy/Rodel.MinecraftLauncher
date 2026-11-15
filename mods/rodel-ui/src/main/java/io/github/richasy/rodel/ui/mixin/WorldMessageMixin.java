package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import java.util.OptionalDouble;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import io.github.richasy.rodel.ui.settings.DialogProgressHost;
import net.minecraft.client.gui.screens.GenericMessageScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(GenericMessageScreen.class)
public abstract class WorldMessageMixin implements DialogTextHost, DialogProgressHost {
    @Override public List<Component> rodel$dialogText() { return List.of(); }
    @Override public Component rodel$progressStatus() { return Component.empty(); }
    @Override public OptionalDouble rodel$progressValue() { return OptionalDouble.empty(); }
}
