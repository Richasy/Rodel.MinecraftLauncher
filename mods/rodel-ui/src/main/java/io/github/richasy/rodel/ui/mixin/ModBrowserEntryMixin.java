package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import com.terraformersmc.modmenu.gui.widget.entries.ModListEntry;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import io.github.richasy.rodel.ui.compat.ModBrowserRow;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.ObjectSelectionList;
import net.minecraft.client.input.MouseButtonEvent;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyConstant;
import org.spongepowered.asm.mixin.injection.Redirect;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.entries.ModListEntry", remap = false)
public abstract class ModBrowserEntryMixin extends ObjectSelectionList.Entry<ModListEntry> {
    @Shadow(remap = false) @Final protected ModListWidget list;

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$drawRow(GuiGraphicsExtractor graphics, int mouseX, int mouseY, boolean hovered,
                               float partialTick, CallbackInfo callback) {
        var browser = ((ModBrowserList) list).rodel$browser();
        if (browser == null) return;
        var entry = (ModListEntry) (Object) this;
        ModBrowserRow.measure(entry, browser).draw(graphics, entry, browser, mouseX, mouseY, hovered);
        callback.cancel();
    }

    @Override
    public boolean isMouseOver(double mouseX, double mouseY) {
        var browser = ((ModBrowserList) list).rodel$browser();
        return browser == null ? super.isMouseOver(mouseX, mouseY)
            : ModBrowserRow.measure((ModListEntry) (Object) this, browser).contains(mouseX, mouseY);
    }

    @ModifyConstant(method = "mouseClicked", constant = {@Constant(intValue = 32), @Constant(intValue = 19)})
    private int rodel$iconSize(int nativeSize) {
        var browser = ((ModBrowserList) list).rodel$browser();
        return browser == null ? nativeSize : ModBrowserRow.iconSize(browser.rodel$modernContext().density());
    }

    @Redirect(method = "mouseClicked", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/input/MouseButtonEvent;x()D"))
    private double rodel$iconHit(MouseButtonEvent event) {
        var browser = ((ModBrowserList) list).rodel$browser();
        return browser == null ? event.x()
            : ModBrowserRow.measure((ModListEntry) (Object) this, browser).nativeClickX(event, list);
    }
}
