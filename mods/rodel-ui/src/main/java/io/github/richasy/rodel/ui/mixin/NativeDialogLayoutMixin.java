package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.DialogHost;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.ConfirmScreen",
    "net.minecraft.client.gui.screens.BackupConfirmScreen",
    "net.minecraft.client.gui.screens.DisconnectedScreen",
    "net.minecraft.client.gui.screens.GenericMessageScreen",
    "net.minecraft.client.gui.screens.GenericWaitingScreen",
    "net.minecraft.client.gui.screens.worldselection.OptimizeWorldScreen",
    "net.minecraft.client.gui.screens.NoticeWithLinkScreen",
    "net.minecraft.client.gui.screens.multiplayer.WarningScreen",
    "net.minecraft.client.gui.screens.options.UnsupportedGraphicsWarningScreen",
    "net.minecraft.client.gui.screens.CreditsAndAttributionScreen",
    "net.minecraft.client.gui.screens.telemetry.TelemetryInfoScreen",
    "com.mojang.realmsclient.gui.screens.RealmsGenericErrorScreen",
    "com.mojang.realmsclient.gui.screens.RealmsConfirmScreen",
    "com.mojang.realmsclient.gui.screens.RealmsClientOutdatedScreen",
    "com.mojang.realmsclient.gui.screens.RealmsParentalConsentScreen"
})
public abstract class NativeDialogLayoutMixin {
    @Inject(method = {"init", "repositionElements"}, at = @At("HEAD"), require = 0)
    private void rodel$virtualLayout(CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog != null) dialog.prepare();
    }
}
