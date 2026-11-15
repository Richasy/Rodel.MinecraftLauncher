package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.SpecialSettingsHost;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.client.gui.screens.options.controls.KeyBindsScreen;
import net.minecraft.client.gui.screens.options.controls.KeyBindsList;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(KeyBindsScreen.class)
public abstract class KeyBindsSettingsMixin implements SpecialSettingsHost {
    @Shadow private KeyBindsList keyBindsList;

    @Override
    public AbstractSelectionList<?> rodel$nativeList() { return keyBindsList; }
}
