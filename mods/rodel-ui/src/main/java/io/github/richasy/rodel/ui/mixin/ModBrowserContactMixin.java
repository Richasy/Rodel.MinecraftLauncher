package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.DescriptionListWidget;
import io.github.richasy.rodel.ui.compat.ModBrowserDetails;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.DescriptionListWidget$MailableContactEntry", remap = false)
public abstract class ModBrowserContactMixin implements ModBrowserDetails.Contact {
    @Shadow(remap = false) @Final private DescriptionListWidget this$0;

    @Redirect(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;text(Lnet/minecraft/client/gui/Font;Lnet/minecraft/network/chat/Component;III)V"))
    private void rodel$contactGlyph(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int color) {
        var browser = ((ModBrowserList) this$0).rodel$browser();
        if (browser == null) graphics.text(font, text, x, y, color);
        else {
            var entry = (AbstractSelectionList.Entry<?>) (Object) this;
            // The native suffix starts at getContentX(), but its name starts at getX().
            // Measure both from the name's actual origin, at the same density.
            int left = entry.getX() + ModBrowserDetails.textWidth(browser, font,
                ((ModBrowserDetails.EntryText) this).rodel$detailText()) + 1;
            ModBrowserDetails.text(browser, graphics, font, text.getVisualOrderText(), left,
                ModBrowserDetails.textY(browser, entry), browser.theme().muted());
        }
    }
}
