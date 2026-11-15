package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.SpecialSettingsHost;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.screens.options.LanguageSelectScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.gui.components.events.GuiEventListener;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyArg;

@Mixin(LanguageSelectScreen.class)
public abstract class LanguageSettingsMixin implements SpecialSettingsHost {
    @Shadow private LanguageSelectScreen.LanguageSelectionList languageSelectionList;

    @Override
    public AbstractSelectionList<?> rodel$nativeList() { return languageSelectionList; }

    @ModifyArg(method = "setInitialFocus", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/options/LanguageSelectScreen;setInitialFocus(Lnet/minecraft/client/gui/components/events/GuiEventListener;)V"), index = 0)
    private GuiEventListener rodel$searchFocus(GuiEventListener widget) {
        NativeSettingsPage page = NativeSettingsPage.get((Screen) (Object) this);
        return page == null ? widget : page.focusTarget(widget);
    }
}
