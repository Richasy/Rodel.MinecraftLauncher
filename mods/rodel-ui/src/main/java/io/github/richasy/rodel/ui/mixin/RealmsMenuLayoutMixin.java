package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "com.mojang.realmsclient.RealmsMainScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsConfigureWorldScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsBackupScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteCodesScreen",
    "com.mojang.realmsclient.gui.screens.RealmsPendingInvitesScreen",
    "com.mojang.realmsclient.gui.screens.RealmsSelectWorldTemplateScreen",
    "net.minecraft.client.gui.screens.multiplayer.JoinMultiplayerScreen",
    "net.minecraft.client.gui.screens.social.SocialInteractionsScreen",
    "net.minecraft.client.gui.screens.ManageServerScreen",
    "net.minecraft.client.gui.screens.DirectJoinServerScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsInviteScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsSlotOptionsScreen",
    "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen"
})
public abstract class RealmsMenuLayoutMixin {
    @Inject(method = {"repositionElements", "updateLayout", "onTabSelected", "onRealmsDataFetched"}, at = @At("RETURN"), require = 0)
    private void rodel$layout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) page.initialized();
    }
}
