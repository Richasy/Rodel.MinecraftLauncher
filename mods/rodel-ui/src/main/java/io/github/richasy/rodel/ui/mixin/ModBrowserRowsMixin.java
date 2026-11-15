package io.github.richasy.rodel.ui.mixin;

import com.terraformersmc.modmenu.config.ModMenuConfig;
import io.github.richasy.rodel.ui.compat.ModernModsScreen;
import io.github.richasy.rodel.ui.compat.ModBrowserRow;
import io.github.richasy.rodel.ui.compat.ModBrowserDetails;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Pseudo;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;

@Pseudo
@Mixin(targets = "com.terraformersmc.modmenu.gui.ModsScreen", remap = false)
public abstract class ModBrowserRowsMixin {
    @ModifyArg(method = "init", at = @At(value = "INVOKE",
        target = "Lcom/terraformersmc/modmenu/gui/widget/ModListWidget;<init>(Lnet/minecraft/client/Minecraft;IIIILcom/terraformersmc/modmenu/gui/widget/ModListWidget;Lcom/terraformersmc/modmenu/gui/ModsScreen;)V"),
        index = 4)
    private int rodel$rowHeight(int nativeHeight) {
        if (!((Object) this instanceof ModernModsScreen browser)) return nativeHeight;
        return ModBrowserRow.rowHeight(browser.rodel$modernContext().density());
    }

    @ModifyArg(method = "init", at = @At(value = "INVOKE",
        target = "Lcom/terraformersmc/modmenu/gui/widget/DescriptionListWidget;<init>(Lnet/minecraft/client/Minecraft;IIIILcom/terraformersmc/modmenu/gui/widget/DescriptionListWidget;Lcom/terraformersmc/modmenu/gui/ModsScreen;)V"),
        index = 4)
    private int rodel$descriptionRowHeight(int nativeHeight) {
        return (Object) this instanceof ModernModsScreen browser ? ModBrowserDetails.rowHeight(browser) : nativeHeight;
    }
}
