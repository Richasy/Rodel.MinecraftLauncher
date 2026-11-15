package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.gui.ModsScreen;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.compat.ModBrowserList;
import io.github.richasy.rodel.ui.compat.ModernModsScreen;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Pseudo
@Mixin(targets = {
    "com.terraformersmc.modmenu.gui.widget.ModListWidget",
    "com.terraformersmc.modmenu.gui.widget.DescriptionListWidget"
}, remap = false)
public abstract class ModBrowserListMixin<E extends AbstractSelectionList.Entry<E>>
    extends AbstractSelectionList<E> implements ModBrowserList {
    // Multi-target validation reads the field flag independently of @Mixin(remap = false).
    @Shadow(remap = false) @Final private ModsScreen parent;

    protected ModBrowserListMixin(Minecraft minecraft, int width, int height, int top, int rowHeight) {
        super(minecraft, width, height, top, rowHeight);
    }

    @Override
    public ModernModsScreen rodel$browser() {
        return parent instanceof ModernModsScreen browser ? browser : null;
    }

    @Inject(method = "<init>", at = @At("RETURN"))
    private void rodel$topAligned(CallbackInfo callback) {
        if (rodel$browser() != null) centerListVertically = false;
    }

    @Inject(method = "scrollBarX", at = @At("HEAD"), cancellable = true)
    private void rodel$fixedScrollbar(CallbackInfoReturnable<Integer> callback) {
        if (rodel$browser() != null) callback.setReturnValue(getRight() - 5);
    }

    @Override
    protected void extractListBackground(GuiGraphicsExtractor graphics) {
        if (rodel$browser() == null) super.extractListBackground(graphics);
    }

    @Override
    protected void extractListSeparators(GuiGraphicsExtractor graphics) {
        if (rodel$browser() == null) super.extractListSeparators(graphics);
    }

    @Override
    protected void extractSelection(GuiGraphicsExtractor graphics, E entry, int color) {
        ModernModsScreen browser = rodel$browser();
        if (browser == null) super.extractSelection(graphics, entry, color);
        else if (isFocused() && browser.rodel$modernContext().focusVisible()) {
            graphics.outline(entry.getX(), entry.getY(), entry.getWidth(), entry.getHeight(), browser.theme().focus());
        }
    }

    @Override
    public int scrollbarWidth() { return rodel$browser() == null ? super.scrollbarWidth() : 3; }

    @Override
    protected void extractScrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        ModernModsScreen browser = rodel$browser();
        if (browser == null) {
            super.extractScrollbar(graphics, mouseX, mouseY);
            return;
        }
        if (maxScrollAmount() <= 0) return;
        MenuTheme theme = browser.theme();
        int x = scrollBarX();
        int trackHeight = getHeight();
        int thumbHeight = Math.min(trackHeight, scrollerHeight());
        int thumbY = getY() + Math.round((trackHeight - thumbHeight) * (float) (scrollAmount() / maxScrollAmount()));
        graphics.fill(x, getY(), x + scrollbarWidth(), getBottom(), theme.edge());
        graphics.fill(x, thumbY, x + scrollbarWidth(), thumbY + thumbHeight, theme.muted());
    }
}
