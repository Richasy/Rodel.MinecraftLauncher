package io.github.richasy.rodel.ui.mixin;

import io.github.richasy.rodel.ui.compat.ArtifactBlueprintFonts;
import io.github.richasy.rodel.ui.UiContexts;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;
import org.spongepowered.asm.mixin.Mixin;
import org.spongepowered.asm.mixin.Unique;
import org.spongepowered.asm.mixin.injection.At;
import org.spongepowered.asm.mixin.injection.ModifyVariable;

@Mixin(Font.class)
public abstract class SettingsFontMixin {
    @ModifyVariable(method = {"lambda$new$0", "getGlyph"}, at = @At("HEAD"), argsOnly = true)
    private Style rodel$blueprintWeight(Style style) {
        return ArtifactBlueprintFonts.resolveWeight(style);
    }

    @Unique private static final FontDescription rodel$uiFont = new FontDescription.Resource(
        Identifier.fromNamespaceAndPath("rodel_game_ui", "chinese"));
    @Unique private static final FontDescription rodel$blueprintFont = new FontDescription.Resource(
        Identifier.fromNamespaceAndPath("rodel_game_ui", "regular"));

    @ModifyVariable(method = "getGlyphSource", at = @At("HEAD"), argsOnly = true)
    private FontDescription rodel$settingsFont(FontDescription source) {
        Minecraft minecraft = Minecraft.getInstance();
        if (minecraft != null && minecraft.gui != null
            && source instanceof FontDescription.Resource resource
            && (resource.id().equals(Identifier.withDefaultNamespace("default"))
                || resource.id().equals(Identifier.withDefaultNamespace("uniform")))) {
            // Blueprint keeps native Component weights, Font measurements and EditBox input.
            // Use the regular face so its explicit bold headings do not become double-bold.
            if (ArtifactBlueprintFonts.applies(minecraft.gui.screen())) return rodel$blueprintFont;
            if (minecraft.gui.screen() instanceof io.github.richasy.rodel.ui.ModernScreen screen && screen.rodel$regularText()) return rodel$blueprintFont;
            if (UiContexts.modern(minecraft.gui.screen()) != null) return rodel$uiFont;
        }
        return source;
    }
}
