package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import io.github.richasy.rodel.ui.workstation.WorkstationFilterButton;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeLayout;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.navigation.ScreenRectangle;
import net.minecraft.client.gui.screens.recipebook.RecipeBookComponent;
import net.minecraft.client.gui.screens.recipebook.RecipeBookTabButton;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(RecipeBookComponent.class)
abstract class WorkstationRecipePanelMixin {
    @Shadow private int width;
    @Shadow private int height;
    @Shadow private boolean widthTooNarrow;
    @Shadow private EditBox searchBox;
    @Shadow protected CycleButton<Boolean> filterButton;
    @Shadow private ScreenRectangle magnifierIconPlacement;
    @Shadow @Final private List<RecipeBookTabButton> tabButtons;
    @Shadow public abstract boolean isVisible();

    @Unique private boolean rodel$active() {
        return WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen()) != null;
    }

    @Inject(method = "getXOrigin", at = @At("HEAD"), cancellable = true)
    private void rodel$horizontalOrigin(CallbackInfoReturnable<Integer> cir) {
        if (rodel$active()) cir.setReturnValue(WorkstationRecipeLayout.left(width, widthTooNarrow));
    }

    @Inject(method = "getYOrigin", at = @At("HEAD"), cancellable = true)
    private void rodel$alignRecipePanel(CallbackInfoReturnable<Integer> cir) {
        if (rodel$active()) {
            cir.setReturnValue((height - WorkstationSurface.HEIGHT) / 2);
        }
    }

    @Inject(method = "initVisuals", at = @At("TAIL"))
    private void rodel$layoutSearch(CallbackInfo ci) {
        if (!rodel$active()) return;
        int x = WorkstationRecipeLayout.left(width, widthTooNarrow);
        int y = (height - WorkstationSurface.HEIGHT) / 2;
        searchBox.setPosition(x + 24, y + 8);
        searchBox.setSize(91, 10);
        searchBox.setBordered(false);
        searchBox.setTextShadow(false);
        searchBox.setTextColor(WorkstationRecipeLayout.INK);
        // The game's hint overload forces an italic style and shadow. Keep the actual EditBox
        // for text, cursor and IME, and paint only its empty, unfocused placeholder with the page.
        searchBox.setHint(Component.empty());
        magnifierIconPlacement = new ScreenRectangle(x + 6, y + 4, 18, 16);
        filterButton.setPosition(x + 122, y + 4);
        filterButton.setSize(20, 16);
        ((WorkstationFilterButton) filterButton).rodel$styleRecipeFilter();
    }

    @Inject(method = "updateTabs", at = @At("TAIL"))
    private void rodel$integrateCategories(boolean filtering, CallbackInfo ci) {
        if (!rodel$active()) return;
        int x = WorkstationRecipeLayout.left(width, widthTooNarrow);
        int y = (height - WorkstationSurface.HEIGHT) / 2 + WorkstationRecipeLayout.GRID_Y;
        for (RecipeBookTabButton tab : tabButtons) {
            if (!tab.visible) continue;
            tab.setPosition(x + 6, y);
            tab.setSize(26, 25);
            y += WorkstationRecipeLayout.PITCH;
        }
    }

    @Inject(method = "hasClickedOutside", at = @At("HEAD"), cancellable = true)
    private void rodel$protectPageBounds(double mx, double my, int left, int top, int w, int h,
                                        CallbackInfoReturnable<Boolean> cir) {
        if (!rodel$active() || !isVisible()) return;
        int x = WorkstationRecipeLayout.left(width, widthTooNarrow);
        int y = (height - WorkstationSurface.HEIGHT) / 2;
        boolean inBook = mx >= x && mx < x + WorkstationRecipeLayout.WIDTH
            && my >= y && my < y + WorkstationSurface.HEIGHT;
        boolean inPanel = mx >= left && mx < left + w && my >= top && my < top + h;
        cir.setReturnValue(!inBook && !inPanel);
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blit(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIFFIIII)V"))
    private void rodel$drawRecipePanel(GuiGraphicsExtractor g, RenderPipeline pipeline, Identifier texture,
                                       int x, int y, float u, float v, int w, int h, int textureWidth, int textureHeight) {
        WorkstationSurface surface = WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen());
        if (surface == null) {
            g.blit(pipeline, texture, x, y, u, v, w, h, textureWidth, textureHeight);
        } else {
            WorkstationRecipeLayout.background(g, x, y, searchBox.isFocused());
            if (searchBox.getValue().isEmpty() && !searchBox.isFocused()) {
                g.text(Minecraft.getInstance().font, Component.translatable("gui.recipebook.search_hint"),
                    x + 24, y + 8, WorkstationRecipeLayout.MUTED, false);
            }
        }
    }
}
