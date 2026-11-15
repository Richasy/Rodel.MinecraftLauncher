package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = {
    "com.mojang.realmsclient.RealmsMainScreen$RealmSelectionList",
    "com.mojang.realmsclient.gui.screens.RealmsPdpScreen$PdpList",
    "com.mojang.realmsclient.gui.screens.RealmsPendingInvitesScreen$PendingInvitationSelectionList",
    "com.mojang.realmsclient.gui.screens.RealmsSelectWorldTemplateScreen$WorldTemplateList",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsBackupScreen$BackupObjectSelectionList",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteCodesScreen$InviteCodeSelectionList",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsPlayersTab$InvitedObjectSelectionList",
    "net.minecraft.client.gui.screens.reporting.ChatSelectionScreen$ChatSelectionList",
    "net.minecraft.client.gui.screens.reporting.ReportReasonSelectionScreen$ReasonSelectionList"
})
public abstract class RealmsMenuListMixin {
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$width(CallbackInfoReturnable<Integer> callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null)
            callback.setReturnValue(Math.max(32, ((AbstractSelectionList<?>) (Object) this).getWidth() - 20));
    }
}
