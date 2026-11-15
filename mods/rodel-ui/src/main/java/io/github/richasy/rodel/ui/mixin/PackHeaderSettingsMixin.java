package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsRow;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.packs.TransferableSelectionList$HeaderEntry")
public abstract class PackHeaderSettingsMixin implements NativeSettingsRow {
    @Override public int rodel$height(NativeSettingsPage page) { return 0; }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$fixedPackHeader(CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.ownsEntry(this)) callback.cancel();
    }
}
