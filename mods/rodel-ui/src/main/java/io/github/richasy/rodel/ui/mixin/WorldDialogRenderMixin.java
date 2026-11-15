package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.DialogHost;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.BackupConfirmScreen",
    "net.minecraft.client.gui.screens.GenericWaitingScreen",
    "net.minecraft.client.gui.screens.worldselection.OptimizeWorldScreen"
})
public abstract class WorldDialogRenderMixin {
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog == null) return;
        dialog.render(graphics, mouseX, mouseY, partialTick);
        callback.cancel();
    }
}
