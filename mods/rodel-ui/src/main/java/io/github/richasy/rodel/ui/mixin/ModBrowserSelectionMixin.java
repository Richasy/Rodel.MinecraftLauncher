package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import com.terraformersmc.modmenu.gui.widget.entries.ModListEntry;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import io.github.richasy.rodel.ui.compat.ModBrowserRow;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.ModifyArg;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.widget.ModListWidget", remap = false)
public abstract class ModBrowserSelectionMixin {
    @Inject(method = "drawSelectionHighlight", at = @At("HEAD"), cancellable = true)
    private void rodel$selection(GuiGraphicsExtractor graphics, int x, int y, int width, int height,
                                int outerColor, int innerColor, CallbackInfo callback) {
        var browser = ((ModBrowserList) this).rodel$browser();
        if (browser == null) return;
        var theme = browser.theme();
        var list = (ModListWidget) (Object) this;
        var row = ModBrowserRow.measure(list.getSelected(), browser);
        graphics.fill(row.left(), row.top(), row.right(), row.top() + row.height(), theme.selection());
        graphics.outline(row.left(), row.top(), row.right() - row.left(), row.height(), theme.edge());
        if (((AbstractWidget) (Object) this).isFocused() && browser.rodel$modernContext().focusVisible()) {
            graphics.outline(row.left() + 1, row.top() + 1, row.right() - row.left() - 2, row.height() - 2, theme.focus());
        }
        callback.cancel();
    }

    @Inject(method = "getRowLeft", at = @At("HEAD"), cancellable = true)
    private void rodel$rowInset(CallbackInfoReturnable<Integer> callback) {
        if (((ModBrowserList) this).rodel$browser() != null) {
            callback.setReturnValue(((AbstractWidget) (Object) this).getX()
                + ((ModBrowserList) this).rodel$browser().rodel$modernContext().density().inset());
        }
    }

    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rowWidth(CallbackInfoReturnable<Integer> callback) {
        if (((ModBrowserList) this).rodel$browser() != null) {
            callback.setReturnValue(Math.max(1, ((AbstractWidget) (Object) this).getWidth()
                - ((ModBrowserList) this).rodel$browser().rodel$modernContext().density().inset() * 2 - 7));
        }
    }

    @Inject(method = "getEntryAtPos", at = @At("HEAD"), cancellable = true)
    private void rodel$paddedEntry(double mouseX, double mouseY, CallbackInfoReturnable<ModListEntry> callback) {
        if (((ModBrowserList) this).rodel$browser() == null) return;
        ModListWidget list = (ModListWidget) (Object) this;
        if (list.isMouseOver(mouseX, mouseY) && mouseX < list.getRight() - 5) {
            var entries = list.children();
            int low = 0;
            int high = entries.size() - 1;
            while (low <= high) {
                int middle = (low + high) >>> 1;
                ModListEntry entry = entries.get(middle);
                if (mouseY < entry.getY()) high = middle - 1;
                else if (mouseY >= entry.getY() + entry.getHeight()) low = middle + 1;
                else {
                    // ParentEntry.isMouseOver calls getEntryAtPos again; use our painted bounds directly.
                    var row = ModBrowserRow.measure(entry, ((ModBrowserList) this).rodel$browser());
                    callback.setReturnValue(row.contains(mouseX, mouseY) ? entry : null);
                    return;
                }
            }
        }
        callback.setReturnValue(null);
    }
}
