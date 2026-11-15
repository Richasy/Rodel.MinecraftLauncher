package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchDetail;
import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen$DetailsScreen$PackListEntry")
public abstract class WorldBranchPackEntryMixin implements WorldBranchDetail {
    @Shadow @Final private Component packId;
    @Shadow @Final private Component message;
    @Override public Component rodel$branchHeading() { return packId; }
    @Override public Component rodel$branchDescription() { return message; }
    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$details(GuiGraphicsExtractor graphics, int x, int y, boolean hovered,
                               float partialTick, CallbackInfo callback) {
        var page = WorldBranchPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) return;
        page.detail(graphics, (AbstractSelectionList.Entry<?>) (Object) this, this);
        callback.cancel();
    }
}
