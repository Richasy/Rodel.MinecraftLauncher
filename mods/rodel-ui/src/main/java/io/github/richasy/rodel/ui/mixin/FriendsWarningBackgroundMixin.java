package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.UiContexts;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.friends.FriendsListConfirmScreen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(FriendsListConfirmScreen.class)
public abstract class FriendsWarningBackgroundMixin {
    @Inject(method = "extractBackground", at = @At("HEAD"), cancellable = true)
    private void rodel$ownedBackground(CallbackInfo callback) {
        if (UiContexts.modern((Screen) (Object) this) != null) callback.cancel();
    }
}
