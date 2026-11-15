package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen$DetailsScreen$PackList")
public abstract class WorldBranchPackListMixin {
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$width(CallbackInfoReturnable<Integer> callback) {
        var page = WorldBranchPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.owns(this))
            callback.setReturnValue(((AbstractSelectionList<?>) (Object) this).getWidth() - 20);
    }
}
