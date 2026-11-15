package io.github.richasy.rodel.ui.mixin;

import com.mojang.realmsclient.dto.RealmsServer;
import com.mojang.realmsclient.gui.screens.configuration.RealmsConfigureWorldScreen;
import io.github.richasy.rodel.ui.menus.NetworkMenuLayouts;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.tabs.MenuTabBar;
import net.minecraft.client.gui.components.tabs.TabManager;
import net.minecraft.client.gui.layouts.HeaderAndFooterLayout;
import com.mojang.renderpearl.api.pipeline.RenderPipeline;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(RealmsConfigureWorldScreen.class)
public abstract class RealmsMenuManagerLayoutMixin implements NetworkMenuLayouts.RealmManager {
    @Shadow @Final private TabManager tabManager;
    @Shadow private MenuTabBar tabNavigationBar;
    @Shadow @Final HeaderAndFooterLayout layout;
    @Shadow private Button playButton;
    @Shadow private RealmsServer serverData;

    @Override public TabManager rodel$tabs() { return tabManager; }
    @Override public MenuTabBar rodel$bar() { return tabNavigationBar; }
    @Override public HeaderAndFooterLayout rodel$layout() { return layout; }
    @Override public Button rodel$play() { return playButton; }
    @Override public RealmsServer rodel$server() { return serverData; }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;blit(Lcom/mojang/renderpearl/api/pipeline/RenderPipeline;Lnet/minecraft/resources/Identifier;IIFFIIII)V"))
    private void rodel$footer(GuiGraphicsExtractor graphics, RenderPipeline pipeline, Identifier texture,
                              int x, int y, float u, float v, int width, int height, int textureWidth, int textureHeight) {
        if (io.github.richasy.rodel.ui.menus.NativeMenuPage.get((net.minecraft.client.gui.screens.Screen) (Object) this) == null)
            graphics.blit(pipeline, texture, x, y, u, v, width, height, textureWidth, textureHeight);
    }
}
