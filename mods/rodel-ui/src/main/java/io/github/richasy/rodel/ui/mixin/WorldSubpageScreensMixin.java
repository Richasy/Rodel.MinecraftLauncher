package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen",
    "net.minecraft.client.gui.screens.worldselection.ExperimentsScreen",
    "net.minecraft.client.gui.screens.packs.PackSelectionScreen"
})
public abstract class WorldSubpageScreensMixin {
    @Inject(method = "init", at = @At("HEAD"))
    private void rodel$prepare(CallbackInfo callback) {
        var page = WorldSubpage.get((Screen) (Object) this);
        if (page != null) page.beginInit();
    }
    @Inject(method = "init", at = @At("RETURN"))
    private void rodel$initialized(CallbackInfo callback) {
        var page = WorldSubpage.get((Screen) (Object) this);
        if (page != null) page.initialized();
    }
    @Inject(method = "repositionElements", at = @At("HEAD"))
    private void rodel$prepareResize(CallbackInfo callback) {
        var page = WorldSubpage.get((Screen) (Object) this);
        if (page != null) page.prepare();
    }
    @Inject(method = "repositionElements", at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        var page = WorldSubpage.get((Screen) (Object) this);
        if (page != null) page.layout();
    }
}
