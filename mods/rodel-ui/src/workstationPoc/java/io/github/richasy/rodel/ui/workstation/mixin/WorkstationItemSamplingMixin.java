package io.github.richasy.rodel.ui.workstation.mixin;

import com.mojang.blaze3d.systems.RenderSystem;
import com.mojang.renderpearl.api.textures.FilterMode;
import com.mojang.renderpearl.api.textures.GpuSampler;
import com.mojang.renderpearl.api.textures.GpuTextureView;
import io.github.richasy.rodel.ui.workstation.WorkstationItemState;
import net.minecraft.client.gui.render.GuiItemAtlas;
import net.minecraft.client.gui.render.GuiRenderer;
import net.minecraft.client.gui.render.TextureSetup;
import net.minecraft.client.renderer.state.gui.GuiItemRenderState;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(GuiRenderer.class)
abstract class WorkstationItemSamplingMixin {
    @Redirect(method = "submitBlitFromItemAtlas", at = @At(value = "INVOKE",
        target = "Lnet/minecraft/client/gui/render/TextureSetup;singleTexture(Lcom/mojang/renderpearl/api/textures/GpuTextureView;Lcom/mojang/renderpearl/api/textures/GpuSampler;)Lnet/minecraft/client/gui/render/TextureSetup;"))
    private TextureSetup rodel$filterCompactIcon(GpuTextureView texture, GpuSampler sampler,
                                                GuiItemRenderState item, GuiItemAtlas.SlotView slot) {
        // Only the final 16 -> 12 reduction is filtered. Minecraft still renders the model
        // and its pixel textures normally; inventory items retain their original sampler.
        if (((WorkstationItemState) (Object) item).rodel$isCompactIcon()) {
            sampler = RenderSystem.getSamplerCache().getRepeat(FilterMode.LINEAR);
        }
        return TextureSetup.singleTexture(texture, sampler);
    }
}
