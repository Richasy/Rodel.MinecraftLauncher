package io.github.richasy.rodel.ui.mixin;

import com.llamalad7.mixinextras.injector.wrapmethod.WrapMethod;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTextTooltip;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(ClientTextTooltip.class)
public abstract class MenuTooltipMixin {
    @WrapMethod(method = "extractText")
    private void rodel$nativeTooltipInk(GuiGraphicsExtractor graphics, Font font, int x, int y, Operation<Void> original) {
        var page = NativeMenuPage.get(Minecraft.getInstance().gui.screen());
        if (page == null || !page.rendering()) {
            original.call(graphics, font, x, y);
            return;
        }
        page.beginNativeText();
        try {
            original.call(graphics, font, x, y);
        } finally {
            page.endNativeText();
        }
    }
}
