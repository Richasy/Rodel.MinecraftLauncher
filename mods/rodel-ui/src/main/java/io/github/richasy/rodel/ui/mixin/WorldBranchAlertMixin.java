package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchTextHost;
import net.minecraft.client.gui.screens.AlertScreen;
import net.minecraft.network.chat.Component;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(AlertScreen.class)
public interface WorldBranchAlertMixin extends WorldBranchTextHost {
    @Override @Accessor("messageText") Component rodel$branchMessage();
}
