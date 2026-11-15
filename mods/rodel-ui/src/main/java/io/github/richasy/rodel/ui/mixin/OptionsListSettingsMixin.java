package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.components.ContainerObjectSelectionList;
import net.minecraft.client.gui.screens.options.OptionsSubScreen;
import net.minecraft.client.Minecraft;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

@Mixin(OptionsList.class)
public abstract class OptionsListSettingsMixin extends ContainerObjectSelectionList<OptionsList.AbstractEntry> {
    @Shadow @Final private OptionsSubScreen screen;

    protected OptionsListSettingsMixin(Minecraft minecraft, int width, int height, int top, int rowHeight) {
        super(minecraft, width, height, top, rowHeight);
    }

    @Inject(method = "getRowWidth", at = @At("HEAD"), cancellable = true)
    private void rodel$rowWidth(CallbackInfoReturnable<Integer> callback) {
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        if (page != null) callback.setReturnValue(page.rowWidth());
    }

    @Override
    protected void extractListBackground(GuiGraphicsExtractor graphics) {
        if (NativeSettingsPage.get(screen) == null) super.extractListBackground(graphics);
    }

    @Override
    protected void extractListSeparators(GuiGraphicsExtractor graphics) {
        if (NativeSettingsPage.get(screen) == null) super.extractListSeparators(graphics);
    }

    @Override
    public int scrollbarWidth() {
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        return page == null ? super.scrollbarWidth() : Math.max(2, page.p(6));
    }

    @Override
    protected void extractScrollbar(GuiGraphicsExtractor graphics, int mouseX, int mouseY) {
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        if (page == null) {
            super.extractScrollbar(graphics, mouseX, mouseY);
        } else {
            page.scrollbar(graphics, scrollBarX(), getY(), scrollbarWidth(), getHeight(),
                scrollerHeight(), scrollAmount(), maxScrollAmount());
        }
    }
}
