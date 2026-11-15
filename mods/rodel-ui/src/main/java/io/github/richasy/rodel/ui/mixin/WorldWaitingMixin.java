package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import java.util.OptionalDouble;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import io.github.richasy.rodel.ui.settings.DialogProgressHost;
import net.minecraft.client.gui.screens.GenericWaitingScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(GenericWaitingScreen.class)
public abstract class WorldWaitingMixin implements DialogTextHost, DialogProgressHost {
    @Shadow @Final private Component messageText;
    @Shadow @Final private boolean showLoadingDots;
    @Override public List<Component> rodel$dialogText() { return List.of(messageText); }
    @Override public Component rodel$progressStatus() { return Component.empty(); }
    @Override public OptionalDouble rodel$progressValue() {
        return showLoadingDots ? OptionalDouble.empty() : OptionalDouble.of(1);
    }
}
