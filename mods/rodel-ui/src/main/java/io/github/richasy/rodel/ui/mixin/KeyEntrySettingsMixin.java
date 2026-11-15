package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeSettingsRow;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.Button;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(targets = "net.minecraft.client.gui.screens.options.controls.KeyBindsList$KeyEntry")
public abstract class KeyEntrySettingsMixin implements NativeSettingsRow {
    @Shadow @Final private Component name;
    @Shadow @Final private Button changeButton;
    @Shadow @Final private Button resetButton;
    @Shadow private boolean hasCollision;

    @Override
    public int rodel$height(NativeSettingsPage page) { return page.bindingHeight(name, changeButton, hasCollision); }

    @Inject(method = "extractContent", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/options/controls/KeyBindsList$KeyEntry;updateChangeButtonMessage()V",
        shift = At.Shift.AFTER), cancellable = true)
    private void rodel$key(GuiGraphicsExtractor graphics, int mouseX, int mouseY,
                           boolean hovered, float partialTick, CallbackInfo callback) {
        NativeSettingsPage page = NativeSettingsPage.get(Minecraft.getInstance().gui.screen());
        if (page != null && page.ownsEntry(this)) {
            page.bindingRow(graphics, (AbstractSelectionList.Entry<?>) (Object) this, name,
                changeButton, resetButton, hasCollision, mouseX, mouseY, partialTick);
            callback.cancel();
        }
    }
}
