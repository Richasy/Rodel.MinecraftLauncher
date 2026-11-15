package io.github.richasy.rodel.ui.mixin;

import java.util.List;
import java.util.OptionalDouble;
import io.github.richasy.rodel.ui.settings.DialogHost;
import io.github.richasy.rodel.ui.settings.DialogTextHost;
import io.github.richasy.rodel.ui.settings.DialogProgressHost;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.LevelLoadingScreen;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.multiplayer.LevelLoadTracker;
import net.minecraft.network.chat.Component;
import net.minecraft.util.Util;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

@Mixin(LevelLoadingScreen.class)
public abstract class WorldLevelLoadingMixin extends Screen implements DialogTextHost, DialogProgressHost {
    @Shadow @Final private static Component DOWNLOADING_TERRAIN_TEXT;
    @Shadow @Final private static Component READY_TO_PLAY_TEXT;
    @Shadow private LevelLoadTracker loadTracker;
    @Shadow private long lastNarration;
    protected WorldLevelLoadingMixin(Component title) { super(title); }
    @Override public List<Component> rodel$dialogText() { return List.of(); }
    @Override public Component rodel$dialogTitle(Component title) { return DOWNLOADING_TERRAIN_TEXT; }
    @Override public Component rodel$progressStatus() {
        return loadTracker.isLevelReady() ? READY_TO_PLAY_TEXT : Component.empty();
    }
    @Override public OptionalDouble rodel$progressValue() {
        // Server generation at 100% does not mean the client is ready to enter.
        return loadTracker.hasProgress() && loadTracker.serverProgress() < 1
            ? OptionalDouble.of(loadTracker.serverProgress()) : OptionalDouble.empty();
    }
    @Inject(method = "extractRenderState", at = @At("HEAD"), cancellable = true)
    private void rodel$render(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float partialTick, CallbackInfo callback) {
        var dialog = ((DialogHost) this).rodel$dialog();
        if (dialog == null) return;
        long now = Util.getMillis();
        if (now - lastNarration > 2000) {
            lastNarration = now;
            triggerImmediateNarration(true);
        }
        dialog.render(graphics, mouseX, mouseY, partialTick);
        callback.cancel();
    }
}
