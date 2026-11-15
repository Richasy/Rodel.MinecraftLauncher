package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationFilterButton;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.CycleButton;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(CycleButton.class)
abstract class WorkstationRecipeFilterMixin implements WorkstationFilterButton {
    @Unique private boolean rodel$recipeFilter;

    /** {@inheritDoc} */
    @Override public void rodel$styleRecipeFilter() {
        rodel$recipeFilter = true;
    }

    @Inject(method = "extractContents", at = @At("HEAD"), cancellable = true)
    private void rodel$drawFilter(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (!rodel$recipeFilter) return;
        AbstractWidget widget = (AbstractWidget) (Object) this;
        boolean selected = Boolean.TRUE.equals(((CycleButton<?>) (Object) this).getValue());
        WorkstationRecipeLayout.filter(g, widget.getX(), widget.getY(), selected, widget.isHovered(),
            widget.isFocused() && Minecraft.getInstance().getLastInputType().isKeyboard());
        ci.cancel();
    }
}
