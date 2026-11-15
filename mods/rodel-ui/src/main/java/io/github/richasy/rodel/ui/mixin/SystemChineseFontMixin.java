package io.github.richasy.rodel.ui.mixin;

import com.llamalad7.mixinextras.injector.ModifyExpressionValue;
import io.github.richasy.rodel.ui.config.UiConfig;
import io.github.richasy.rodel.ui.config.UiConfig.Effect;
import java.io.IOException;
import java.io.InputStream;
import java.nio.file.Files;
import java.nio.file.Path;
import net.minecraft.client.gui.font.providers.TrueTypeGlyphProviderDefinition;
import net.minecraft.resources.Identifier;
import net.minecraft.server.packs.resources.ResourceManager;
import org.slf4j.LoggerFactory;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Final;
import org.spongepowered.asm.mixin.Shadow;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.Redirect;

@Mixin(TrueTypeGlyphProviderDefinition.class)
public abstract class SystemChineseFontMixin {
    @Shadow @Final private Identifier location;

    @ModifyExpressionValue(method = "load", at = @At(value = "FIELD",
        target = "Lnet/minecraft/client/gui/font/providers/TrueTypeGlyphProviderDefinition;oversample:F"))
    private float rodel$oversample(float configured) {
        return location.getNamespace().equals("rodel_game_ui") && !UiConfig.enabled(Effect.FONT_SMOOTHING)
            ? Math.min(4, configured) : configured;
    }

    @Redirect(
        method = "load",
        at = @At(
            value = "INVOKE",
            target = "Lnet/minecraft/server/packs/resources/ResourceManager;open(Lnet/minecraft/resources/Identifier;)Ljava/io/InputStream;"
        )
    )
    private InputStream rodel$systemFont(ResourceManager resources, Identifier location) throws IOException {
        String filename;
        if (location.equals(Identifier.fromNamespaceAndPath("rodel_game_ui", "font/system_chinese.ttc"))) {
            filename = "msyhbd.ttc";
        } else if (location.equals(Identifier.fromNamespaceAndPath("rodel_game_ui", "font/system_ui.ttf"))) {
            filename = "segoeuib.ttf";
        } else if (location.equals(Identifier.fromNamespaceAndPath("rodel_game_ui", "font/system_chinese_regular.ttc"))) {
            filename = "msyh.ttc";
        } else if (location.equals(Identifier.fromNamespaceAndPath("rodel_game_ui", "font/system_ui_regular.ttf"))) {
            filename = "segoeui.ttf";
        } else {
            return resources.open(location);
        }
        String windows = System.getenv("WINDIR");
        if (windows == null || windows.isBlank()) {
            throw new IOException("Microsoft YaHei requires Windows; the menu will use Minecraft's fallback font.");
        }
        Path font = Path.of(windows, "Fonts", filename);
        InputStream stream = Files.newInputStream(font);
        LoggerFactory.getLogger("Rodel UI").info("Using Windows system font {} for Rodel UI.", filename);
        return stream;
    }
}
