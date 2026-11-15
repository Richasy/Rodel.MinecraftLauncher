package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.AdvancementsMenuPage;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.screens.advancements.AdvancementWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.Constant;
import org.spongepowered.asm.mixin.injection.ModifyConstant;

@Mixin(AdvancementWidget.class)
public abstract class MenuAdvancementTooltipMixin {
    @ModifyConstant(method = "extractHover", constant = @Constant(intValue = 113))
    private int rodel$height(int original) {
        return NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof AdvancementsMenuPage page
            ? page.treeHeight() : original;
    }
}
