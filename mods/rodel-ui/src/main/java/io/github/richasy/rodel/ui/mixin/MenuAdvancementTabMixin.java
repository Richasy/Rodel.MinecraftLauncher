package io.github.richasy.rodel.ui.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.menus.AdvancementsMenuPage;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.advancements.AdvancementTab;
import net.minecraft.client.gui.screens.advancements.AdvancementsScreen;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyConstant;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(AdvancementTab.class)
public abstract class MenuAdvancementTabMixin {
    @Shadow @Final private AdvancementsScreen screen;
    @Shadow private double scrollX;
    @Shadow private double scrollY;
    @Unique private int rodel$width;
    @Unique private int rodel$height;

    @Unique private AdvancementsMenuPage rodel$page() {
        return NativeMenuPage.get(screen) instanceof AdvancementsMenuPage page ? page : null;
    }

    @ModifyConstant(method = {"tick", "extractContents", "extractTooltips", "scroll", "canScrollHorizontally"},
        constant = @Constant(intValue = 234))
    private int rodel$width(int original) { var page = rodel$page(); return page == null ? original : page.treeWidth(); }

    @ModifyConstant(method = {"tick", "extractContents", "extractTooltips", "scroll", "canScrollVertically"},
        constant = @Constant(intValue = 113))
    private int rodel$height(int original) { var page = rodel$page(); return page == null ? original : page.treeHeight(); }

    @ModifyConstant(method = "extractContents", constant = @Constant(intValue = 117))
    private int rodel$centerX(int original) { var page = rodel$page(); return page == null ? original : page.treeWidth() / 2; }

    @ModifyConstant(method = "extractContents", constant = @Constant(intValue = 56))
    private int rodel$centerY(int original) { var page = rodel$page(); return page == null ? original : page.treeHeight() / 2; }

    @Inject(method = "extractContents", at = @At("HEAD"))
    private void rodel$resize(GuiGraphicsExtractor graphics, int x, int y, CallbackInfo callback) {
        var page = rodel$page();
        if (page == null) return;
        if (rodel$width > 0) {
            scrollX += (page.treeWidth() - rodel$width) / 2d;
            scrollY += (page.treeHeight() - rodel$height) / 2d;
        }
        rodel$width = page.treeWidth();
        rodel$height = page.treeHeight();
    }

    @Redirect(method = "extractContents", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blit(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIFFIIII)V"))
    private void rodel$background(GuiGraphicsExtractor graphics, RenderPipeline pipeline, Identifier texture,
                                  int x, int y, float u, float v, int width, int height, int textureWidth, int textureHeight) {
        if (rodel$page() == null) graphics.blit(pipeline, texture, x, y, u, v, width, height, textureWidth, textureHeight);
    }
}
