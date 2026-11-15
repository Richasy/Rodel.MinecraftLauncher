package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import net.minecraft.client.gui.screens.multiplayer.SafetyScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(SafetyScreen.class)
public abstract class SafetyDialogMixin implements DialogTextHost {
    @Shadow @Final private static Component CONTENT;

    @Override public List<Component> rodel$dialogText() { return List.of(CONTENT); }
}
