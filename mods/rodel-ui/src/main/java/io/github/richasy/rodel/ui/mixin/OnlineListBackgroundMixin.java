package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.social.SocialInteractionsPlayerList",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsPlayersTab$InvitedObjectSelectionList"
})
public abstract class OnlineListBackgroundMixin {
    @Inject(method = "extractListBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$background(CallbackInfo callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null) callback.cancel();
    }
}
