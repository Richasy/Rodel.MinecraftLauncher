package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.settings.NativeSettingsPage;
import io.github.richasy.rodel.ui.settings.NativeOptionRows;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.OptionsList;
import net.minecraft.client.gui.screens.Screen;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(OptionsList.Entry.class)
public abstract class OptionsEntrySettingsMixin implements NativeOptionRows {
    @Shadow @Final private List<OptionsList.OptionInstanceWidget> children;
    @Shadow @Final private Screen screen;

    @Override
    public List<OptionsList.OptionInstanceWidget> rodel$options() {
        return children;
    }

    @Redirect(
        method = "extractContent",
        at = @At(value = "INVOKE", target = "Lnet/minecraft/client/gui/components/AbstractWidget;extractRenderState(Lnet/minecraft/client/gui/GuiGraphicsExtractor;IIF)V")
    )
    private void rodel$positionActualWidget(AbstractWidget widget, GuiGraphicsExtractor graphics,
                                             int mouseX, int mouseY, float partialTick) {
        NativeSettingsPage page = NativeSettingsPage.get(screen);
        if (page != null) {
            for (int i = 0; i < children.size(); i++) {
                if (children.get(i).widget() == widget) {
                    page.optionRow(graphics, (OptionsList.Entry) (Object) this, widget, mouseX, mouseY);
                    break;
                }
            }
        }
        widget.extractRenderState(graphics, mouseX, mouseY, partialTick);
    }
}
