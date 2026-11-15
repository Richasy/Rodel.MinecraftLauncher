package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsRow;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.FocusableTextWidget;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.options.controls.KeyBindsList$CategoryEntry")
public abstract class KeyCategorySettingsMixin implements NativeSettingsRow {
    @Shadow @Final private FocusableTextWidget categoryName;

    @Override
    public int rodel$height(NativeSettingsPage page) { return page.p(52); }

    @Inject(method = "extractContent", at = @At("HEAD"), cancellable = true)
    private void rodel$category(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                                boolean hovered, float partialTick, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.ownsEntry(this)) {
            page.group(graphics, (AbstractSelectionList.Entry<?>) (Object) this, categoryName);
            callback.cancel();
        }
    }
}
