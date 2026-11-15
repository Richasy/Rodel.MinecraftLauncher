package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractSelectionList.class)
public abstract class WorldBranchListMixin {
    @Shadow protected boolean centerListVertically;

    private WorldBranchPage rodel$branch() {
        var page = WorldBranchPage.get(Minecraft.getInstance().gui.screen());
        return page != null && page.owns(this) ? page : null;
    }
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$width(CallbackInfoReturnable<Integer> callback) {
        if (rodel$branch() != null) callback.setReturnValue(((AbstractSelectionList<?>) (Object) this).getWidth() - 20);
    }
    @Inject(method = "getRowLeft", at = @At("HEAD"), cancellable = true)
    private void rodel$left(CallbackInfoReturnable<Integer> callback) {
        if (rodel$branch() != null) callback.setReturnValue(((AbstractSelectionList<?>) (Object) this).getX() + 8);
    }
    @Inject(method = "scrollBarX", at = @At("HEAD"), cancellable = true)
    private void rodel$scrollbar(CallbackInfoReturnable<Integer> callback) {
        if (rodel$branch() != null) callback.setReturnValue(((AbstractSelectionList<?>) (Object) this).getRight() - 6);
    }
    @Inject(method = {"extractListBackground", "extractListSeparators"}, at = @At("HEAD"), cancellable = true)
    private void rodel$background(GuiGraphicsExtractor graphics, CallbackInfo callback) {
        if (rodel$branch() != null) callback.cancel();
    }
    @Inject(method = "extractSelection", at = @At("HEAD"), cancellable = true)
    private void rodel$selection(GuiGraphicsExtractor graphics, AbstractSelectionList.Entry<?> entry,
                                int color, CallbackInfo callback) {
        var page = rodel$branch();
        if (page == null) return;
        graphics.fill(entry.getX(), entry.getY(), entry.getX() + entry.getWidth(),
            entry.getY() + entry.getHeight(), page.theme().selection());
        graphics.outline(entry.getX(), entry.getY(), entry.getWidth(), entry.getHeight(), page.theme().edge());
        callback.cancel();
    }
    @Inject(method = "extractWidgetRenderState", at = @At("HEAD"))
    private void rodel$rows(GuiGraphicsExtractor graphics, int x, int y, float partialTick, CallbackInfo callback) {
        var page = rodel$branch();
        if (page == null) return;
        centerListVertically = false;
        page.refreshRows((AbstractSelectionList<?>) (Object) this);
    }
}
