package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "com.mojang.realmsclient.gui.screens.configuration.RealmsPlayersTab$InvitedObjectSelectionList",
    "com.mojang.realmsclient.gui.screens.configuration.RealmsWorldsTab"
})
public abstract class RealmsTabDataLayoutMixin {
    @Inject(method = {"populateList", "updateData"}, at = @At("RETURN"), require = 0)
    private void rodel$dataChanged(CallbackInfo callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.screen() instanceof NetworkMenuLayouts.RealmManager) NetworkMenuLayouts.layout(page);
    }
}
