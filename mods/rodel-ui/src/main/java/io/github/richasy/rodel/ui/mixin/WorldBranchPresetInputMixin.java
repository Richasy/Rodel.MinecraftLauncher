package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.worlds.WorldBranchPage;
import io.github.richasy.rodel.ui.settings.NativeTextBox;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.components.EditBox;
import net.minecraft.client.gui.screens.PresetFlatWorldScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Coerce;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(PresetFlatWorldScreen.class)
public abstract class WorldBranchPresetInputMixin extends Screen implements WorldBranchPage.NativeInputHost {
    protected WorldBranchPresetInputMixin(Component title) { super(title); }

    @Override public void rodel$replaceInput(EditBox editor, NativeTextBox adapter) {
        for (var child : List.copyOf(children())) {
            if (child == editor || child instanceof NativeTextBox box && box.editor() == editor) removeWidget(child);
        }
        addWidget(adapter);
    }

    @Redirect(method = "mouseScrolled", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/screens/PresetFlatWorldScreen$PresetsList;mouseScrolled(DDDD)Z"))
    private boolean rodel$scroll(@Coerce AbstractSelectionList<?> list, double x, double y, double horizontal, double vertical) {
        var page = WorldBranchPage.get((Screen) (Object) this);
        return list.mouseScrolled(page == null ? x : page.coordinate(x),
            page == null ? y : page.coordinate(y), horizontal, vertical);
    }
}
