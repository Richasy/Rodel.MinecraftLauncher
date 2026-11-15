package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.screens.multiplayer.ServerSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(ServerSelectionList.class)
public abstract class OnlineServerListMixin {
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rows(CallbackInfoReturnable<Integer> callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) != null)
            callback.setReturnValue(Math.max(32, ((AbstractSelectionList<?>) (Object) this).getWidth() - 20));
    }
}
