package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import com.mojang.realmsclient.gui.screens.RealmsGenericErrorScreen;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(RealmsGenericErrorScreen.class)
public abstract class RealmsErrorDialogMixin implements DialogTextHost {
    @Shadow @Final private Component detail;

    @Override public List<Component> rodel$dialogText() { return List.of(detail); }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$error(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog != null) {
            dialog.render(graphics, mouseX, mouseY, partialTick);
            callback.cancel();
        }
    }
}
