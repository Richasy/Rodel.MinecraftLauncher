package io.github.richasy.bettertools.mixin;

import io.github.richasy.bettertools.ToolKind;
import io.github.richasy.bettertools.client.ToolOutlineTexture;
import net.minecraft.client.renderer.texture.TextureManager;
import net.minecraft.server.packs.resources.ResourceManager;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Inject;
import org.spongepowered.asm.mixin.injection.callback.CallbackInfo;

/** Adds derived textures before the first native resource reload; later F3+T/pack changes rebuild them automatically. */
@Mixin(TextureManager.class)
public abstract class ToolOutlineTexturesMixin {
    @Inject(method = "<init>", at = @At("RETURN"))
    private void bettertools$outlines(ResourceManager resources, CallbackInfo ci) {
        var textures = (TextureManager)(Object)this;
        for (var kind : ToolKind.values()) textures.register(ToolOutlineTexture.id(kind), new ToolOutlineTexture(kind));
    }
}
