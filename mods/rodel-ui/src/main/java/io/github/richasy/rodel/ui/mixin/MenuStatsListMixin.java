package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.menus.StatsMenuPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.components.AbstractWidget;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(targets = {
    "net.minecraft.client.gui.screens.achievement.StatsScreen$GeneralStatisticsList",
    "net.minecraft.client.gui.screens.achievement.StatsScreen$MobsStatisticsList"
})
public abstract class MenuStatsListMixin {
    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$statsRowWidth(CallbackInfoReturnable<Integer> callback) {
        if (NativeMenuPage.get(Minecraft.getInstance().gui.screen()) instanceof StatsMenuPage)
            callback.setReturnValue(((AbstractWidget) (Object) this).getWidth() - 20);
    }
}
