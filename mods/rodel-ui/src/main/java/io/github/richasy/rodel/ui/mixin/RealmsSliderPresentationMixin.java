package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.settings.NativeOptionPresentation;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(targets = "com.mojang.realmsclient.gui.screens.configuration.RealmsSlotOptionsScreen$SettingsSlider")
public abstract class RealmsSliderPresentationMixin implements NativeOptionPresentation {
    @Override public Component rodel$caption() { return Component.translatable("mco.configure.world.spawnProtection"); }
    @Override public Component rodel$valueText() { return ((AbstractWidget) (Object) this).getMessage(); }
}
