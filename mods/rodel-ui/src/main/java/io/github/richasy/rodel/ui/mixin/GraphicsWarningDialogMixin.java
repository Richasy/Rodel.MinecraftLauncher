package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.options.UnsupportedGraphicsWarningScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(UnsupportedGraphicsWarningScreen.class)
public abstract class GraphicsWarningDialogMixin implements DialogTextHost {
    @Shadow @Final private List<Component> message;
    @Override public List<Component> rodel$dialogText() {
        // Native messages include standalone newline separators; the dialog owns paragraph spacing.
        return message.stream().filter(paragraph -> !paragraph.getString().isBlank()).toList();
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$warning(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog != null) {
            dialog.render(graphics, mouseX, mouseY, partialTick);
            callback.cancel();
        }
    }
}
