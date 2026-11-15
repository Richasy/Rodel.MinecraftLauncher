package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import io.github.richasy.rodel.ui.settings.NativeSettingsList;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.util.FormattedCharSequence;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(targets = "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen$RuleList")
public abstract class WorldRuleListMixin<E extends AbstractSelectionList.Entry<E>> extends AbstractSelectionList<E> implements NativeSettingsList {
    protected WorldRuleListMixin(Minecraft minecraft, int width, int height, int top, int rowHeight) {
        super(minecraft, width, height, top, rowHeight);
    }
    private WorldSubpage rodel$page() {
        var page = WorldSubpage.get(Minecraft.getInstance().gui.screen());
        return page != null && page.ownsList(this) ? page : null;
    }
    @Override public void rodel$topAligned() { centerListVertically = false; }
    @Override public int getRowWidth() { return rodel$page() == null ? super.getRowWidth() : getWidth() - 28; }
    @Override protected void extractListBackground(GuiGraphicsExtractor graphics) {
        if (rodel$page() == null) super.extractListBackground(graphics);
    }
    @Override protected void extractListSeparators(GuiGraphicsExtractor graphics) {
        if (rodel$page() == null) super.extractListSeparators(graphics);
    }
    @Override public int scrollbarWidth() { return rodel$page() == null ? super.scrollbarWidth() : 3; }
    @Redirect(method = "extractWidgetRenderState", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/GuiGraphicsExtractor;setTooltipForNextFrame(Ljava/util/List;II)V"))
    private void rodel$inlineDetails(GuiGraphicsExtractor graphics, List<FormattedCharSequence> text, int x, int y) {
        if (rodel$page() == null) graphics.setTooltipForNextFrame(text, x, y);
    }
}
