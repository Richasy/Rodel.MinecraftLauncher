package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.screens.recipebook.RecipeBookComponent;
import net.minecraft.client.gui.screens.recipebook.RecipeBookTabButton;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(RecipeBookTabButton.class)
abstract class WorkstationRecipeTabMixin {
    @Shadow @Final private RecipeBookComponent.TabInfo tabInfo;
    @Shadow private boolean selected;
    @Shadow private float animationTime;

    @Inject(method = "extractContents", at = @At("HEAD"), cancellable = true)
    private void rodel$drawIntegratedTab(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        WorkstationSurface surface = WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen());
        if (surface == null) return;
        AbstractWidget tab = (AbstractWidget) (Object) this;
        int x = tab.getX();
        int y = tab.getY();
        surface.recipeTile(g, x, y, tab.getWidth(), tab.getHeight(), selected, false, false,
            tab.isHovered(), tab.isFocused() && Minecraft.getInstance().getLastInputType().isKeyboard());
        if (animationTime > 0) {
            float pulse = 1 + 0.1F * (float) Math.sin(animationTime / 15 * Math.PI);
            g.pose().pushMatrix();
            g.pose().translate(x + 13, y + 12);
            g.pose().scale(1, pulse);
            g.pose().translate(-x - 13, -y - 12);
        }
        if (tabInfo.secondaryIcon().isPresent()) {
            WorkstationSurface.smallItem(g, tabInfo.primaryIcon(), x + 2, y + 4);
            WorkstationSurface.smallItem(g, tabInfo.secondaryIcon().get(), x + 12, y + 9);
        } else {
            g.fakeItem(tabInfo.primaryIcon(), x + 5, y + 4);
        }
        if (animationTime > 0) {
            g.pose().popMatrix();
            animationTime -= delta;
        }
        ci.cancel();
    }
}
