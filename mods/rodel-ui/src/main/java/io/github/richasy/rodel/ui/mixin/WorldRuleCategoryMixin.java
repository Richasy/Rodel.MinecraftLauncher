package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldSubpage;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.FocusableTextWidget;
import net.minecraft.client.gui.screens.options.InWorldGameRulesScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.worldselection.AbstractGameRulesScreen$CategoryRuleEntry")
public abstract class WorldRuleCategoryMixin {
    @Shadow @Final private FocusableTextWidget widget;
    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$category(GuiGraphicsExtractor graphics, int x, int y, boolean hovered, float partialTick, CallbackInfo callback) {
        var page = WorldSubpage.get(Minecraft.getInstance().gui.screen());
        if (page != null) {
            page.categoryRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this, widget);
            callback.cancel();
        } else if (Minecraft.getInstance().gui.screen().getClass() == InWorldGameRulesScreen.class
            && NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof NativeMenuPage menu) {
            var row = (AbstractSelectionList.Entry<?>) (Object) this;
            var label = MenuText.secondary(Component.literal(widget.getMessage().getString()), menu.theme().muted());
            MenuText.extract(graphics, Minecraft.getInstance().font, label,
                row.getContentX() + menu.p(12), row.getY() + row.getHeight() - menu.p(20), menu.p(16), menu.theme().muted());
            callback.cancel();
        }
    }
}
