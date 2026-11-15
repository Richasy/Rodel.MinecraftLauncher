package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsList;
import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.settings.PackSettingsBody;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.options.LanguageSelectScreen$LanguageSelectionList",
    "net.minecraft.client.gui.screens.options.controls.KeyBindsList",
    "net.minecraft.client.gui.screens.packs.TransferableSelectionList"
})
public abstract class SpecialSettingsListMixin<E extends AbstractSelectionList.Entry<E>> extends AbstractSelectionList<E> implements NativeSettingsList {
    protected SpecialSettingsListMixin(Minecraft minecraft, int width, int height, int top, int rowHeight) {
        super(minecraft, width, height, top, rowHeight);
    }

    @Override
    public void rodel$topAligned() { centerListVertically = false; }

    private NativeSettingsPage rodel$page() {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        return page != null && page.ownsList(this) ? page : null;
    }

    private NativeFormContext rodel$context() {
        var page = rodel$page();
        if (page != null) return page;
        var world = WorldSubpage.get(Minecraft.getInstance().gui.screen());
        return world != null && world.ownsList(this) ? world : null;
    }

    private PackSettingsBody rodel$packBrowser() {
        var page = rodel$page();
        if (page != null) return page.packBrowser();
        var world = WorldSubpage.get(Minecraft.getInstance().gui.screen());
        return world != null && world.ownsList(this) ? world.packs() : null;
    }

    @Override public int getRowLeft() {
        var packs = rodel$packBrowser();
        return packs != null && packs.browser() ? packs.rowLeft(this) : super.getRowLeft();
    }

    @Override protected int scrollBarX() {
        var packs = rodel$packBrowser();
        var context = rodel$context();
        return packs != null && packs.browser() ? getRight() - context.p(8) : super.scrollBarX();
    }

    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rowWidth(CallbackInfoReturnable<Integer> callback) {
        NativeSettingsPage page = rodel$page();
        if (page != null) callback.setReturnValue(page.listRowWidth(this));
        else if (rodel$context() instanceof WorldSubpage world) callback.setReturnValue(world.listRowWidth(this));
    }

    @Override
    protected void extractListBackground(GuiGraphicsExtractor graphics) {
        if (rodel$context() == null) super.extractListBackground(graphics);
    }

    @Override
    protected void extractListSeparators(GuiGraphicsExtractor graphics) {
        if (rodel$context() == null) super.extractListSeparators(graphics);
    }

    @Override
    protected void extractSelection(GuiGraphicsExtractor graphics, E entry, int color) {
        if (rodel$context() == null) super.extractSelection(graphics, entry, color);
    }

    @Override
    public int scrollbarWidth() {
        NativeFormContext page = rodel$context();
        return page == null ? super.scrollbarWidth() : Math.max(2, page.p(6));
    }

    @Override
    protected void extractScrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        NativeFormContext page = rodel$context();
        if (page == null) super.extractScrollbar(graphics, mouseX, mouseY);
        else page.scrollbar(graphics, scrollBarX(), getY(), scrollbarWidth(), getHeight(),
            scrollerHeight(), scrollAmount(), maxScrollAmount());
    }
}
