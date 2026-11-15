package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationBookButton;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeState;
import io.github.richasy.rodel.ui.workstation.WorkstationRecipeLayout;
import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.gui.components.ImageButton;
import net.minecraft.client.gui.components.Tooltip;
import net.minecraft.client.gui.components.events.GuiEventListener;
import net.minecraft.client.gui.navigation.ScreenPosition;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.client.gui.screens.inventory.AbstractRecipeBookScreen;
import net.minecraft.client.gui.screens.recipebook.RecipeBookComponent;
import net.minecraft.network.chat.Component;
import net.minecraft.world.entity.player.Inventory;
import net.minecraft.world.inventory.RecipeBookMenu;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(AbstractRecipeBookScreen.class)
abstract class WorkstationRecipeMixin extends AbstractContainerScreen<RecipeBookMenu> implements WorkstationRecipeState {
    @Shadow @Final private RecipeBookComponent<?> recipeBookComponent;
    @Shadow private boolean widthTooNarrow;
    @Shadow protected abstract ScreenPosition getRecipeBookButtonPosition();

    protected WorkstationRecipeMixin(RecipeBookMenu menu, Inventory inventory, Component title) {
        super(menu, inventory, title);
    }

    @Redirect(method = {"init", "lambda$initButton$0"}, at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/screens/recipebook/RecipeBookComponent;updateScreenPosition(II)I"))
    private int rodel$keepBookGutter(RecipeBookComponent<?> book, int width, int panelWidth) {
        if (WorkstationSurface.forScreen(this) != null && book.isVisible() && !widthTooNarrow) {
            return WorkstationRecipeLayout.left(width, false) + WorkstationRecipeLayout.WIDTH + WorkstationRecipeLayout.GAP;
        }
        return book.updateScreenPosition(width, panelWidth);
    }

    @Redirect(method = {"initButton", "lambda$initButton$0"}, at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/screens/inventory/AbstractRecipeBookScreen;getRecipeBookButtonPosition()Lnet/minecraft/client/gui/navigation/ScreenPosition;"))
    private ScreenPosition rodel$positionBookButton(AbstractRecipeBookScreen<?> screen) {
        if (WorkstationSurface.forScreen(this) == null) return getRecipeBookButtonPosition();
        return new ScreenPosition(leftPos + WorkstationSurface.BOOK_X, topPos + WorkstationSurface.BOOK_Y);
    }

    /** {@inheritDoc} */
    @Override public boolean rodel$recipeOnly() {
        return widthTooNarrow && recipeBookComponent.isVisible();
    }

    @ModifyArg(method = "initButton", at = @At(value = "INVOKE", target =
        "Lnet/minecraft/client/gui/screens/inventory/AbstractRecipeBookScreen;addRenderableWidget(Lnet/minecraft/client/gui/components/events/GuiEventListener;)Lnet/minecraft/client/gui/components/events/GuiEventListener;"))
    private GuiEventListener rodel$styleBookButton(GuiEventListener widget) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface != null && widget instanceof ImageButton button) {
            ((WorkstationBookButton) button).rodel$bind(surface, recipeBookComponent);
            button.setSize(18, 18);
            Component label = Component.translatable("rodel.workstation.recipe_book");
            button.setMessage(label);
            button.setTooltip(Tooltip.create(label));
        }
        return widget;
    }
}
