package io.github.richasy.rodel.ui.mixin;

import java.util.ArrayList;
import java.util.List;
import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.CreateFlatWorldScreen$DetailsList$LayerEntry",
    "net.minecraft.client.gui.screens.CreateFlatWorldScreen$DetailsList$HeaderEntry",
    "net.minecraft.client.gui.screens.PresetFlatWorldScreen$PresetsList$Entry",
    "net.minecraft.client.gui.screens.CreateBuffetWorldScreen$BiomeList$Entry"
})
public abstract class WorldBranchEntryTextMixin {
    @Unique private final List<WorldBranchPage.NativeRowText> rodel$textRuns = new ArrayList<>();

    @Inject(method = "extractContent", at = @At("HEAD"))
    private void rodel$beginText(GuiGraphicsExtractor graphics, int x, int y, boolean hovered, float tick, CallbackInfo callback) {
        rodel$textRuns.clear();
    }

    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$text(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        var page = WorldBranchPage.get(Minecraft.getInstance().gui.screen());
        if (page == null) graphics.text(font, text, x, y, color);
        else rodel$textRuns.add(new WorldBranchPage.NativeRowText(text, x, color));
    }

    @Inject(method = "extractContent", at = @At("RETURN"))
    private void rodel$finishText(GuiGraphicsExtractor graphics, int x, int y, boolean hovered, float tick, CallbackInfo callback) {
        var page = WorldBranchPage.get(Minecraft.getInstance().gui.screen());
        if (page != null) page.nativeRowText(graphics, (AbstractSelectionList.Entry<?>) (Object) this, rodel$textRuns, hovered);
    }
}
