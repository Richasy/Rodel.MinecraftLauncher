package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationBookButton;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeLayout;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.ImageButton;
import net.minecraft.client.gui.screens.recipebook.RecipeBookPage;
import net.minecraft.client.gui.screens.recipebook.RecipeButton;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(RecipeBookPage.class)
abstract class WorkstationRecipePageMixin {
    @Shadow @Final private List<RecipeButton> buttons;
    @Shadow private ImageButton forwardButton;
    @Shadow private ImageButton backButton;
    @Shadow private int currentPage;
    @Shadow private int totalPages;

    @Unique private boolean rodel$active() {
        return WorkstationSurface.forScreen(Minecraft.getInstance().gui.screen()) != null;
    }

    @Inject(method = "init", at = @At("TAIL"))
    private void rodel$layoutPage(Minecraft minecraft, int x, int y, CallbackInfo ci) {
        if (!rodel$active()) return;
        for (int i = 0; i < buttons.size(); i++) {
            buttons.get(i).setPosition(x + WorkstationRecipeLayout.GRID_X + i % WorkstationRecipeLayout.COLUMNS * WorkstationRecipeLayout.PITCH,
                y + WorkstationRecipeLayout.GRID_Y + i / WorkstationRecipeLayout.COLUMNS * WorkstationRecipeLayout.PITCH);
        }
        backButton.setPosition(x + 48, y + WorkstationSurface.HEIGHT - 23);
        forwardButton.setPosition(x + 113, y + WorkstationSurface.HEIGHT - 23);
        ((WorkstationBookButton) backButton).rodel$bindPage(false);
        ((WorkstationBookButton) forwardButton).rodel$bindPage(true);
    }

    @Redirect(method = "extractRenderState", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$replacePageLabel(GuiGraphicsExtractor g, Font font, Component text, int x, int y, int color) {
        if (!rodel$active()) g.text(font, text, x, y, color);
    }

    @Inject(method = "extractRenderState", at = @At("HEAD"))
    private void rodel$drawPageLabel(GuiGraphicsExtractor g, int x, int y, int mouseX, int mouseY, float delta, CallbackInfo ci) {
        if (!rodel$active() || totalPages == 0) return;
        Component page = Component.translatable("gui.recipebook.page", currentPage + 1, totalPages);
        Font font = Minecraft.getInstance().font;
        g.text(font, page, x + 86 - font.width(page) / 2, y + WorkstationSurface.HEIGHT - 18, WorkstationRecipeLayout.INK, false);
    }
}
