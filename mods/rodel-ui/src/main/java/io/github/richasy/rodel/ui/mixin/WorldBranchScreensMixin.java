package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.CreateFlatWorldScreen",
    "net.minecraft.client.gui.screens.PresetFlatWorldScreen",
    "net.minecraft.client.gui.screens.CreateBuffetWorldScreen",
    "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen",
    "net.minecraft.client.gui.screens.worldselection.ConfirmExperimentalFeaturesScreen$DetailsScreen",
    "net.minecraft.client.gui.screens.AlertScreen",
    "net.minecraft.client.gui.screens.DatapackLoadFailureScreen",
    "net.minecraft.client.gui.screens.RecoverWorldDataScreen",
    "net.minecraft.client.gui.screens.FileFixerAbortedScreen",
    "net.minecraft.client.gui.screens.worldselection.FileFixerProgressScreen"
})
public abstract class WorldBranchScreensMixin {
    private WorldBranchPage rodel$branch() { return WorldBranchPage.get((Screen) (Object) this); }

    @Inject(method = "init", at = @At("HEAD"))
    private void rodel$prepare(CallbackInfo callback) {
        if (rodel$branch() != null) rodel$branch().beginInit();
    }
    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$initialized(CallbackInfo callback) {
        if (rodel$branch() != null) rodel$branch().initialized();
    }
    @Inject(method = "repositionElements", at = @At("HEAD"), require = 0)
    private void rodel$prepareResize(CallbackInfo callback) {
        if (rodel$branch() != null) rodel$branch().prepare();
    }
    @Inject(method = "repositionElements", at = @At("RETURN"), require = 0)
    private void rodel$layout(CallbackInfo callback) {
        if (rodel$branch() != null) rodel$branch().layout();
    }
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true, require = 0)
    private void rodel$render(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        if (rodel$branch() == null) return;
        rodel$branch().render(graphics, x, y, partialTick);
        callback.cancel();
    }
}
