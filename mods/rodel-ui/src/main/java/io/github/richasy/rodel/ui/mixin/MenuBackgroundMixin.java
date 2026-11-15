package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.PauseScreen",
    "net.minecraft.client.gui.screens.WorldOptionsScreen",
    "net.minecraft.client.gui.screens.achievement.StatsScreen",
    "net.minecraft.client.gui.screens.advancements.AdvancementsScreen",
    "net.minecraft.client.gui.screens.friends.FriendsOverlayScreen",
    "net.minecraft.client.gui.screens.social.SocialInteractionsScreen",
    "net.minecraft.client.gui.components.PopupScreen",
    "com.mojang.realmsclient.RealmsMainScreen",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsConfigureWorldScreen"
})
public abstract class MenuBackgroundMixin {
    @Inject(method = {"extractBackground", "extractMenuBackground"}, at = @At("HEAD"), cancellable = true, require = 0)
    private void rodel$ownedBackground(CallbackInfo callback) {
        if (NativeMenuPage.get((Screen) (Object) this) != null) callback.cancel();
    }
}
