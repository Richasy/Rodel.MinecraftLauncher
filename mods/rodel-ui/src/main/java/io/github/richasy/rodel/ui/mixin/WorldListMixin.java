package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.screens.worldselection.WorldSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(WorldSelectionList.class)
public abstract class WorldListMixin<E extends AbstractSelectionList.Entry<E>> extends AbstractSelectionList<E> {
    protected WorldListMixin(Minecraft minecraft, int width, int height, int y, int rowHeight) {
        super(minecraft, width, height, y, rowHeight);
    }
    private WorldPage rodel$page() {
        var page = WorldPage.get(Minecraft.getInstance().gui.screen());
        return page != null && page.owns(this) ? page : null;
    }
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rowWidth(CallbackInfoReturnable<Integer> callback) {
        if (rodel$page() != null) callback.setReturnValue(getWidth() - 4);
    }
    @Override public int getRowLeft() { return rodel$page() == null ? super.getRowLeft() : getX() - 2; }
    @Override protected int scrollBarX() { return rodel$page() == null ? super.scrollBarX() : getRight() - 4; }
    @Override public int scrollbarWidth() { return rodel$page() == null ? super.scrollbarWidth() : 3; }
    @Override protected void extractListBackground(GuiGraphicsExtractor graphics) {
        if (rodel$page() == null) super.extractListBackground(graphics);
    }
    @Override protected void extractListSeparators(GuiGraphicsExtractor graphics) {
        if (rodel$page() == null) super.extractListSeparators(graphics);
    }
    @Override protected void extractSelection(GuiGraphicsExtractor graphics, E entry, int color) {
        if (rodel$page() == null) super.extractSelection(graphics, entry, color);
    }
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"))
    private void rodel$rows(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        if (rodel$page() == null) return;
        centerListVertically = false;
        boolean changed = false;
        for (E entry : children()) if (entry instanceof WorldSelectionList.WorldListEntry && entry.getHeight() != rodel$page().worldRowHeight()) {
            entry.setHeight(rodel$page().worldRowHeight());
            changed = true;
        }
        if (changed) {
            double scroll = scrollAmount();
            updateSizeAndPosition(getWidth(), getHeight(), getX(), getY());
            setScrollAmount(scroll);
        }
    }
}
