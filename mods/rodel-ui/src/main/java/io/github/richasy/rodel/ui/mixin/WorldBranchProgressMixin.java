package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.worlds.WorldBranchProgressHost;
import net.minecraft.client.gui.screens.worldselection.FileFixerProgressScreen;
import net.minecraft.util.worldupdate.UpgradeProgress;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.gen.Accessor;

@Mixin(FileFixerProgressScreen.class)
public interface WorldBranchProgressMixin extends WorldBranchProgressHost {
    @Override @Accessor("upgradeProgress") UpgradeProgress rodel$branchProgress();
}
