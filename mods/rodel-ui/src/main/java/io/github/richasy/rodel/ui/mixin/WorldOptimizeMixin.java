package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import java.util.OptionalDouble;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import io.github.richasy.rodel.ui.settings.DialogProgressHost;
import net.minecraft.client.gui.screens.worldselection.OptimizeWorldScreen;
import net.minecraft.network.chat.Component;
import net.minecraft.util.Mth;
import net.minecraft.util.worldupdate.WorldUpgrader;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;

@Mixin(OptimizeWorldScreen.class)
public abstract class WorldOptimizeMixin implements DialogTextHost, DialogProgressHost {
    @Shadow @Final private WorldUpgrader upgrader;
    @Override public List<Component> rodel$dialogText() { return List.of(); }
    @Override public Component rodel$progressStatus() { return upgrader.getStatus(); }
    @Override public OptionalDouble rodel$progressValue() {
        return upgrader.getTotalChunks() > 0 ? OptionalDouble.of(upgrader.getTotalProgress()) : OptionalDouble.empty();
    }
    @Override public Component rodel$progressDetail() {
        if (upgrader.getTotalChunks() <= 0) return Component.empty();
        return Component.translatable("optimizeWorld.progress.counter",
            upgrader.getConverted() + upgrader.getSkipped(), upgrader.getTotalChunks())
            .append("  ").append(Component.translatable("optimizeWorld.progress.percentage",
                Mth.floor(upgrader.getTotalProgress() * 100)));
    }
}
