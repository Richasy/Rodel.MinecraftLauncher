package io.github.richasy.rodel.ui.mixin;

import java.util.function.Supplier;
import com.llamalad7.mixinextras.injector.wrapoperation.Operation;
import com.llamalad7.mixinextras.injector.wrapoperation.WrapOperation;
import io.github.richasy.rodel.ui.FontAtlasSampling;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import net.minecraft.client.gui.font.FontTexture;
import net.minecraft.client.gui.font.GlyphRenderTypes;
import net.minecraft.client.gui.font.GlyphStitcher;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;

@Mixin(GlyphStitcher.class)
public abstract class UiGlyphStitcherMixin {
    @Shadow @Final private Identifier texturePrefix;

    @WrapOperation(method = "stitch", at = @At(value = "NEW", target = "net/minecraft/client/gui/font/FontTexture"))
    private FontTexture rodel$fontAtlas(Supplier<String> label, GlyphRenderTypes types, boolean colored,
                                       Operation<FontTexture> original) {
        FontTexture texture = original.call(label, types, colored);
        if (!colored && texturePrefix.getNamespace().equals("rodel_game_ui") && UiConfig.enabled(Effect.FONT_SMOOTHING))
            ((FontAtlasSampling) texture).rodel$useLinearSampling();
        return texture;
    }
}
