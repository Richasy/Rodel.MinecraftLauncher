package io.github.richasy.rodel.ui.mixin;

import com.mojang.realmsclient.gui.screens.configuration.RealmsInviteScreen;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(RealmsInviteScreen.class)
public abstract class RealmsMenuInviteLayoutMixin implements NetworkMenuLayouts.InviteForm {
    @Shadow @Final private static Component NAME_LABEL;
    @Shadow private Component message;

    @Override public Component rodel$nameLabel() { return NAME_LABEL; }
    @Override public Component rodel$status() { return message; }

    @Inject(method = "showMessage", at = @At("RETURN"))
    private void rodel$statusLayout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) NetworkMenuLayouts.layout(page);
    }
}
