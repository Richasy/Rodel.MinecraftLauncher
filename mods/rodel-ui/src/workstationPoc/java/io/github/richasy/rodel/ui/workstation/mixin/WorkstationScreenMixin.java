package io.github.richasy.rodel.ui.workstation.mixin;

import io.github.richasy.rodel.ui.workstation.WorkstationSurface;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.screens.inventory.AbstractContainerScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.world.inventory.AbstractContainerMenu;
import net.minecraft.world.inventory.Slot;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Mutable;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(AbstractContainerScreen.class)
abstract class WorkstationScreenMixin extends Screen {
    @Shadow @Mutable @Final protected int imageWidth;
    @Shadow @Mutable @Final protected int imageHeight;
    @Shadow @Final protected AbstractContainerMenu menu;
    @Shadow @Final protected Component playerInventoryTitle;
    @Shadow protected Slot hoveredSlot;
    @Shadow protected int leftPos;
    @Shadow protected int topPos;
    @Unique private int rodel$originalWidth;
    @Unique private int rodel$originalHeight;
    @Unique private int[] rodel$originalSlots;

    protected WorkstationScreenMixin(Component title) {
        super(title);
    }

    @Inject(method = "isHovering(Lnet/minecraft/world/inventory/Slot;DD)Z", at = @At("HEAD"), cancellable = true)
    private void rodel$hitWorkSlot(Slot slot, double mouseX, double mouseY, CallbackInfoReturnable<Boolean> cir) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface == null) return;
        int size = surface.slotSize(slot, menu);
        if (size <= 18) return;
        int inset = (size - 16) / 2;
        int x = leftPos + slot.x - inset;
        int y = topPos + slot.y - inset;
        cir.setReturnValue(mouseX >= x && mouseX < x + size
            && mouseY >= y && mouseY < y + size);
    }

    @Inject(method = "init", at = @At("HEAD"))
    private void rodel$layout(CallbackInfo ci) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface == null) {
            rodel$restoreLayout();
            return;
        }
        if (rodel$originalSlots == null) {
            rodel$originalWidth = imageWidth;
            rodel$originalHeight = imageHeight;
            rodel$originalSlots = new int[menu.slots.size() * 2];
            for (int i = 0; i < menu.slots.size(); i++) {
                Slot slot = menu.getSlot(i);
                rodel$originalSlots[i * 2] = slot.x;
                rodel$originalSlots[i * 2 + 1] = slot.y;
            }
        }
        imageWidth = WorkstationSurface.WIDTH;
        imageHeight = WorkstationSurface.HEIGHT;
        surface.placeSlots(menu);
    }

    @Inject(method = "removed", at = @At("TAIL"))
    private void rodel$restoreAfterClose(CallbackInfo ci) {
        rodel$restoreLayout();
    }

    @Unique
    private void rodel$restoreLayout() {
        if (rodel$originalSlots == null) return;
        imageWidth = rodel$originalWidth;
        imageHeight = rodel$originalHeight;
        for (int i = 0; i < rodel$originalSlots.length / 2; i++) {
            WorkstationSlotAccess slot = (WorkstationSlotAccess) menu.getSlot(i);
            slot.rodel$setX(rodel$originalSlots[i * 2]);
            slot.rodel$setY(rodel$originalSlots[i * 2 + 1]);
        }
        rodel$originalSlots = null;
    }

    @Inject(method = "extractLabels", at = @At("HEAD"), cancellable = true)
    private void rodel$drawLabels(GuiGraphicsExtractor g, int mouseX, int mouseY, CallbackInfo ci) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface == null) return;
        surface.labels(g, font, title, playerInventoryTitle);
        ci.cancel();
    }

    @Inject(method = "extractSlotHighlightBack", at = @At("HEAD"), cancellable = true)
    private void rodel$drawHighlight(GuiGraphicsExtractor g, CallbackInfo ci) {
        WorkstationSurface surface = WorkstationSurface.forScreen(this);
        if (surface == null) return;
        surface.highlight(g, hoveredSlot, menu);
        ci.cancel();
    }

    @Inject(method = "extractSlotHighlightFront", at = @At("HEAD"), cancellable = true)
    private void rodel$skipVanillaHighlight(GuiGraphicsExtractor g, CallbackInfo ci) {
        if (WorkstationSurface.forScreen(this) != null) ci.cancel();
    }
}
