package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsRow;
import io.github.richasy.rodel.ui.settings.NativeLanguageChoice;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.options.LanguageSelectScreen$LanguageSelectionList$Entry")
public abstract class LanguageEntrySettingsMixin implements NativeSettingsRow, NativeLanguageChoice {
    @Shadow @Final private Component language;
    @Shadow @Final private String code;

    @Override
    public String rodel$languageCode() { return code; }

    @Override
    public int rodel$height(NativeSettingsPage page) { return page.density().control() + page.density().gap() / 2; }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$language(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                boolean hovered, float partialTick, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.ownsEntry(this)) {
            page.languageRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this, language, code, hovered);
            callback.cancel();
        }
    }
}
