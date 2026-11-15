package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.StringWidget;
import net.minecraft.client.gui.screens.worldselection.WorldSelectionList;
import net.minecraft.client.input.MouseButtonEvent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyVariable;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(WorldSelectionList.WorldListEntry.class)
public abstract class WorldEntryMixin {
    @Shadow @Final private WorldSelectionList list;
    @Shadow @Final private StringWidget worldNameText;
    @Shadow @Final private StringWidget infoText;
    @Shadow @Final private StringWidget idAndLastPlayedText;
    private WorldPage rodel$page() {
        var page = WorldPage.get(Minecraft.getInstance().gui.screen());
        return page != null && page.owns(list) ? page : null;
    }
    @Inject(method = "extractContent", at = @At("HEAD"))
    private void rodel$row(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float partialTick, CallbackInfo callback) {
        var page = rodel$page();
        if (page == null) return;
        page.worldRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this,
            worldNameText.getMessage(), infoText.getMessage(), idAndLastPlayedText.getMessage(), hovered);
        graphics.pose().pushMatrix();
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        graphics.pose().translate(entry.getContentX() + page.worldIconInset(), entry.getY() + page.worldIconInset());
        graphics.pose().scale(page.worldIconSize() / 32f);
        graphics.pose().translate(-entry.getContentX(), -entry.getContentY());
    }
    @Inject(method = "extractContent", at = @At("RETURN"))
    private void rodel$endRow(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered, float partialTick, CallbackInfo callback) {
        if (rodel$page() != null) graphics.pose().popMatrix();
    }
    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/components/StringWidget;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V"))
    private void rodel$nativeText(StringWidget widget, GuiGraphicsExtractor graphics, int x, int y, float partialTick) {
        if (rodel$page() == null) widget.extractRenderState(graphics, x, y, partialTick);
    }
    @ModifyVariable(method = "extractContent", at = @At("HEAD"), argsOnly = true, ordinal = 0)
    private int rodel$iconX(int x) {
        var page = rodel$page();
        if (page == null) return x;
        int origin = ((AbstractSelectionList.Entry<?>) (Object) this).getContentX();
        return Math.round(origin + (x - origin - page.worldIconInset()) * 32f / page.worldIconSize());
    }
    @ModifyVariable(method = "extractContent", at = @At("HEAD"), argsOnly = true, ordinal = 1)
    private int rodel$iconY(int y) {
        var page = rodel$page();
        if (page == null) return y;
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        return Math.round(entry.getContentY() + (y - entry.getY() - page.worldIconInset()) * 32f / page.worldIconSize());
    }
    @ModifyVariable(method = "mouseClicked", at = @At("HEAD"), argsOnly = true)
    private MouseButtonEvent rodel$iconHit(MouseButtonEvent event) {
        var page = rodel$page();
        if (page == null) return event;
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        double x = event.x() - entry.getContentX() - page.worldIconInset();
        double y = event.y() - entry.getY() - page.worldIconInset();
        return x >= 0 && y >= 0 && x < page.worldIconSize() && y < page.worldIconSize()
            ? new MouseButtonEvent(entry.getContentX() + x * 32 / page.worldIconSize(),
                entry.getContentY() + y * 32 / page.worldIconSize(), event.buttonInfo())
            : new MouseButtonEvent(entry.getContentX() + 33, event.y(), event.buttonInfo());
    }
}
