package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationBookButton;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeLayout;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.client.gui.components.ImageButton;
import net.minecraft.client.gui.screens.recipebook.RecipeBookComponent;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(ImageButton.class)
abstract class WorkstationBookButtonMixin extends Button implements WorkstationBookButton {
    @Unique private WorkstationSurface rodel$surface;
    @Unique private RecipeBookComponent<?> rodel$book;
    @Unique private boolean rodel$page;
    @Unique private boolean rodel$forward;

    protected WorkstationBookButtonMixin(int x, int y, int width, int height, Component message,
                                         OnPress press, CreateNarration narration) {
        super(x, y, width, height, message, press, narration);
    }

    /** {@inheritDoc} */
    @Override public void rodel$bind(WorkstationSurface surface, RecipeBookComponent<?> book) {
        rodel$surface = surface;
        rodel$book = book;
    }

    @Inject(method = "extractContents", at = @At("HEAD"), cancellable = true)
    private void rodel$drawBookButton(GuiGraphicsExtractor g, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        boolean keyboardFocus = isFocused() && Minecraft.getInstance().getLastInputType().isKeyboard();
        if (rodel$page) {
            WorkstationRecipeLayout.pageArrow(g, getX(), getY(), getWidth(), getHeight(), rodel$forward,
                isHovered(), keyboardFocus);
            ci.cancel();
            return;
        }
        if (rodel$surface == null) return;
        rodel$surface.recipeButton(g, getX(), getY(), getWidth(), getHeight(), rodel$book.isVisible(),
            isHovered(), keyboardFocus);
        ci.cancel();
    }

    /** {@inheritDoc} */
    @Override public void rodel$bindPage(boolean forward) {
        rodel$page = true;
        rodel$forward = forward;
    }
}
