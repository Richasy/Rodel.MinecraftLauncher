package io.github.richasy.rodel.ui.mixin;

import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.textures.FilterMode;
import io.github.richasy.rodel.ui.FontAtlasSampling;
import net.minecraft.client.gui.font.FontTexture;
import net.minecraft.client.renderer.texture.AbstractTexture;
import org.spongepowered.asm.mixin.Mixin;

@Mixin(FontTexture.class)
public abstract class FontAtlasSamplingMixin extends AbstractTexture implements FontAtlasSampling {
    @Override
    public void rodel$useLinearSampling() {
        sampler = RenderSystem.getSamplerCache().getRepeat(FilterMode.LINEAR);
    }
}
