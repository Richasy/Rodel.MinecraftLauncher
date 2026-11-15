package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.DescriptionListWidget;
import com.terraformersmc.modmenu.gui.widget.UpdateAvailableBadge;
import io.github.richasy.rodel.ui.compat.ModBrowserDetails;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import io.github.richasy.rodel.ui.compat.ModBrowserText;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.util.FormattedCharSequence;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.DescriptionListWidget$DescriptionEntry", remap = false)
public abstract class ModBrowserDescriptionMixin implements ModBrowserDetails.EntryText {
    @Shadow(remap = false) @Final private DescriptionListWidget this$0;
    @Shadow(remap = false) protected FormattedCharSequence text;
    @Shadow(remap = false) protected int indent;
    @Shadow(remap = false) public boolean updateTextEntry;
    @Unique private boolean rodel$heading;

    @Override
    public FormattedCharSequence rodel$detailText() { return text; }

    @Inject(method = "<init>(Lcom/terraformersmc/modmenu/gui/widget/DescriptionListWidget;Lnet/minecraft/util/FormattedCharSequence;I)V",
        at = @At("RETURN"))
    private void rodel$regularText(DescriptionListWidget parent, FormattedCharSequence original, int indent,
                                  CallbackInfo callback) {
        if (((ModBrowserList) parent).rodel$browser() != null) {
            text = ModBrowserText.secondary(text);
            this.indent = 0;
            StringBuilder label = new StringBuilder();
            original.accept((index, style, codePoint) -> {
                label.appendCodePoint(codePoint);
                return true;
            });
            String value = label.toString().stripTrailing();
            rodel$heading = value.endsWith(":") || value.endsWith("：");
        }
    }

    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/util/FormattedCharSequence;III)V"))
    private void rodel$detailsInk(GuiGraphicsExtractor graphics, Font font, FormattedCharSequence text, int x, int y, int color) {
        var browser = ((ModBrowserList) this$0).rodel$browser();
        if (browser == null) graphics.text(font, text, x, y, color);
        else {
            var entry = (AbstractSelectionList.Entry<?>) (Object) this;
            int left = entry.getX() + (updateTextEntry ? ModBrowserDetails.updateAdvance(browser) : 0);
            ModBrowserDetails.text(browser, graphics, font, text, left, ModBrowserDetails.textY(browser, entry),
                rodel$heading ? browser.theme().ink() : browser.theme().muted());
        }
    }

    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lcom/terraformersmc/modmenu/gui/widget/UpdateAvailableBadge;renderBadge(Lnet/minecraft/client/gui/GuiGraphicsExtractor;II)V"))
    private void rodel$updateBadge(GuiGraphicsExtractor graphics, int x, int y) {
        var browser = ((ModBrowserList) this$0).rodel$browser();
        if (browser == null) {
            UpdateAvailableBadge.renderBadge(graphics, x, y);
            return;
        }
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        int size = ModBrowserDetails.updateIconSize(browser);
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(entry.getX(), entry.getY() + Math.max(0, (entry.getHeight() - size) / 2));
            graphics.pose().scale(size / (float) ModBrowserDetails.NATIVE_UPDATE_ICON_SIZE);
            UpdateAvailableBadge.renderBadge(graphics, 0, 0);
        } finally {
            graphics.pose().popMatrix();
        }
    }

    @Inject(method = "isMouseOver", at = @At("HEAD"), cancellable = true)
    private void rodel$detailHitArea(double mouseX, double mouseY, CallbackInfoReturnable<Boolean> callback) {
        var browser = ((ModBrowserList) this$0).rodel$browser();
        if (browser == null) return;
        var entry = (AbstractSelectionList.Entry<?>) (Object) this;
        var font = Minecraft.getInstance().font;
        int width = ModBrowserDetails.textWidth(browser, font, text);
        if (updateTextEntry) width += ModBrowserDetails.updateAdvance(browser);
        if (this instanceof ModBrowserDetails.Contact) {
            width += ModBrowserDetails.contactAdvance(browser, font);
        }
        int right = entry.getX() + Math.min(entry.getWidth(), width);
        callback.setReturnValue(mouseX >= entry.getX() && mouseX < right
            && mouseY >= entry.getY() && mouseY < entry.getY() + entry.getHeight());
    }
}
