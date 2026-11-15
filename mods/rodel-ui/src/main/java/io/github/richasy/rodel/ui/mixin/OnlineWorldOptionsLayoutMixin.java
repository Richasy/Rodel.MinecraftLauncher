package io.github.richasy.rodel.ui.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ScrollableLayout;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.WorldOptionsScreen;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(WorldOptionsScreen.class)
public abstract class OnlineWorldOptionsLayoutMixin implements NetworkMenuLayouts.WorldOptions {
    @Shadow private ScrollableLayout scrollArea;
    @Shadow private Button applyChanges;

    @Override public ScrollableLayout rodel$scroll() { return scrollArea; }
    @Override public Button rodel$apply() { return applyChanges; }

    @Inject(method = {"repositionElements", "onGamemasterPermissionChanged", "onDifficultyChanged",
        "updateGuestCommandAccessButton", "updateForceGameModeButton", "updatePortControlsState",
        "updateApplyChangesActiveState", "updatePersonalGameModeButton", "onDefaultGameModeChanged"},
        at = @At("RETURN"))
    private void rodel$layout(CallbackInfo callback) {
        NativeMenuPage page = NativeMenuPage.get((Screen) (Object) this);
        if (page != null) NetworkMenuLayouts.layout(page);
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blit(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIFFIIII)V"))
    private void rodel$separator(GuiGraphicsExtractor graphics, RenderPipeline pipeline, Identifier texture,
                                 int x, int y, float u, float v, int width, int height, int textureWidth, int textureHeight) {
        if (NativeMenuPage.get((Screen) (Object) this) == null)
            graphics.blit(pipeline, texture, x, y, u, v, width, height, textureWidth, textureHeight);
    }
}
