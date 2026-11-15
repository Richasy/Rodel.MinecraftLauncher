package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.ModsScreen;
import io.github.richasy.rodel.ui.compat.ModBrowserText;
import io.github.richasy.rodel.ui.compat.ModernModsScreen;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.util.FormattedCharSequence;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.util.mod.ModBadgeRenderer", remap = false)
public abstract class ModBrowserBadgeMixin {
    @Shadow(remap = false) @Final protected ModsScreen screen;
    @Shadow(remap = false) protected int badgeX;
    @Shadow(remap = false) protected int badgeY;
    @Shadow(remap = false) protected int badgeMax;

    @Inject(method = "drawBadge(Lnet/minecraft/client/gui/GuiGraphicsExtractor;Lnet/minecraft/util/FormattedCharSequence;IIII)V",
        at = @At("HEAD"), cancellable = true)
    private void rodel$compactBadge(GuiGraphicsExtractor graphics, FormattedCharSequence text,
                                   int outlineColor, int fillColor, int mouseX, int mouseY, CallbackInfo callback) {
        if (!(screen instanceof ModernModsScreen browser)) return;
        var font = Minecraft.getInstance().font;
        var label = ModBrowserText.secondary(text);
        boolean tight = browser.rodel$modernContext().density().tight();
        int textHeight = browser.rodel$modernContext().density().detail();
        int horizontalPadding = tight ? 3 : 5;
        int verticalPadding = 1;
        float scale = textHeight / (float) font.lineHeight;
        int width = (int) Math.ceil(font.width(label) * scale) + horizontalPadding * 2;
        int height = textHeight + verticalPadding * 2;
        if (badgeX + width <= badgeMax) {
            int y = badgeY;
            var theme = browser.theme();
            graphics.fill(badgeX, y, badgeX + width, y + height, theme.highContrast() ? theme.surface() : fillColor);
            graphics.outline(badgeX, y, width, height, theme.highContrast() ? theme.edge() : outlineColor);
            graphics.pose().pushMatrix();
            graphics.pose().translate(badgeX + horizontalPadding, y + verticalPadding);
            graphics.pose().scale(scale);
            graphics.text(font, label, 0, 0, theme.highContrast() ? theme.ink() : 0xFFCACACA, false);
            graphics.pose().popMatrix();
            badgeX += width + 3;
        }
        callback.cancel();
    }
}
