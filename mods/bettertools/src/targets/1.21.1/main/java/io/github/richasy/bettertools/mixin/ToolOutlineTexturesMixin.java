package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.ToolKind;
import io.github.richasy.bettertools.client.ToolOutlineTexture;
import java.util.concurrent.CompletableFuture;
import java.util.concurrent.Executor;
import net.minecraft.client.renderer.texture.TextureManager;
import net.minecraft.server.packs.resources.PreparableReloadListener;
import net.minecraft.server.packs.resources.ResourceManager;
import net.minecraft.util.profiling.ProfilerFiller;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfoReturnable;

/** 1.21.1 register loads immediately, so wait for native packs and the renderer before deriving their silhouettes. */
@Mixin(TextureManager.class)
abstract class ToolOutlineTexturesMixin {
    @Inject(method = "reload", at = @At("RETURN"), cancellable = true)
    private void bettertools$outlines(PreparableReloadListener.PreparationBarrier barrier, ResourceManager resources,
            ProfilerFiller preparation, ProfilerFiller application, Executor background, Executor game,
            CallbackInfoReturnable<CompletableFuture<Void>> ci) {
        var textures = (TextureManager)(Object)this;
        ci.setReturnValue(ci.getReturnValue().thenRunAsync(() -> {
            for (var kind : ToolKind.values()) textures.register(ToolOutlineTexture.id(kind), new ToolOutlineTexture(kind));
        }, game));
    }
}
