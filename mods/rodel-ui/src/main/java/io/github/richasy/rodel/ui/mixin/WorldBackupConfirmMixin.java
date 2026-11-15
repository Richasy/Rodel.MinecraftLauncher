package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import net.minecraft.client.gui.screens.BackupConfirmScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(BackupConfirmScreen.class)
public abstract class WorldBackupConfirmMixin implements DialogTextHost {
    @Shadow @Final private Component description;
    @Override public List<Component> rodel$dialogText() { return List.of(description); }
}
