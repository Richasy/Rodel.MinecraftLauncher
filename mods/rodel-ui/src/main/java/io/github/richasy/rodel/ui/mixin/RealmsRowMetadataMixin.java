package io.github.richasy.rodel.ui.mixin;

import java.util.function.Supplier;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.ObjectSelectionList;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = "com.mojang.realmsclient.RealmsMainScreen$Entry")
public abstract class RealmsRowMetadataMixin {
    @Unique private static final int rodel$statusSize = 7;
    @Unique private static final int rodel$rightPadding = 7;
    @Unique private static final int rodel$gap = 6;
    @Unique private static final float rodel$versionScale = .9f;

    @Shadow protected abstract int firstLineY(int top);

    @Inject(method = "versionTextX", at = @At("HEAD"), cancellable = true)
    private void rodel$versionPosition(int left, int width, Component version, CallbackInfoReturnable<Integer> callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) return;
        int versionWidth = (int) Math.ceil(Minecraft.getInstance().font.width(MenuText.secondary(version)) * rodel$versionScale);
        callback.setReturnValue(left + width - rodel$rightPadding - rodel$statusSize - rodel$gap - versionWidth);
    }

    @Inject(method = "extractRealmStatus", at = @At("HEAD"), cancellable = true)
    private void rodel$status(GuiGraphicsExtractor graphics, int nativeX, int nativeY, int mouseX, int mouseY,
                              Identifier sprite, Supplier<Component> tooltip, CallbackInfo callback) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) return;
        var entry = (ObjectSelectionList.Entry<?>) (Object) this;
        int left = entry.getContentRight() - rodel$rightPadding - rodel$statusSize;
        int top = firstLineY(entry.getContentY())
            + Math.round((Minecraft.getInstance().font.lineHeight * rodel$versionScale - rodel$statusSize) / 2);
        graphics.blitSprite(RenderPipelines.GUI_TEXTURED, sprite, left, top, rodel$statusSize, rodel$statusSize);
        if (mouseX >= left && mouseX < left + rodel$statusSize && mouseY >= top && mouseY < top + rodel$statusSize
            && page.screen().children().stream().filter(AbstractSelectionList.class::isInstance)
                .anyMatch(list -> list.isMouseOver(mouseX, mouseY)))
            graphics.setTooltipForNextFrame(tooltip.get(), mouseX, mouseY);
        callback.cancel();
    }
}
