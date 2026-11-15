package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import java.util.OptionalDouble;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import io.github.richasy.rodel.ui.settings.DialogProgressHost;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.ProgressScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ProgressScreen.class)
public abstract class WorldSaveProgressMixin implements DialogTextHost, DialogProgressHost {
    @Shadow private Component header;
    @Shadow private Component stage;
    @Shadow private int progress;
    @Shadow private boolean stop;
    @Override public List<Component> rodel$dialogText() { return List.of(); }
    @Override public Component rodel$progressStatus() { return header == null ? Component.empty() : header; }
    @Override public OptionalDouble rodel$progressValue() {
        return stage != null && progress > 0 ? OptionalDouble.of(progress / 100.0) : OptionalDouble.empty();
    }
    @Override public Component rodel$progressDetail() { return stage == null ? Component.empty() : stage; }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        // The original stop branch owns clearing the screen after saving finishes.
        if (dialog == null || stop) return;
        dialog.render(graphics, mouseX, mouseY, partialTick);
        callback.cancel();
    }
}
