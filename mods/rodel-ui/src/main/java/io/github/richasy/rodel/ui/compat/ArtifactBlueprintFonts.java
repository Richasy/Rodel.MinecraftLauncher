package io.github.richasy.rodel.ui.compat;

import java.util.Set;
import io.github.richasy.rodel.ui.platform.LoaderPlatform;
import net.minecraft.client.gui.screens.Screen;
import net.minecraft.client.Minecraft;
import net.minecraft.network.chat.FontDescription;
import net.minecraft.network.chat.Style;
import net.minecraft.resources.Identifier;

/** Font-only opt-in compatibility; never enrolls these screens in modern layout or input handling. */
public final class ArtifactBlueprintFonts {
    private static final FontDescription BOLD = new FontDescription.Resource(Identifier.fromNamespaceAndPath("rodel_game_ui", "chinese"));
    private static final boolean INSTALLED = LoaderPlatform.isModLoaded("artifact_blueprint");
    private static final Set<String> SCREENS = Set.of(
        "dev.richasy.abp.client.CatalogueScreen",
        "dev.richasy.abp.client.ChoiceScreen",
        "dev.richasy.abp.client.BuildSetupScreen",
        "dev.richasy.abp.client.PreviewScreen",
        "dev.richasy.abp.client.BuildProgressScreen",
        "dev.richasy.abp.client.BuildEndScreen",
        "dev.richasy.abp.client.BuildReviewScreen"
    );

    private ArtifactBlueprintFonts() {}

    public static boolean applies(Screen screen) {
        return INSTALLED && screen != null && SCREENS.contains(screen.getClass().getName());
    }

    /** Keep native semantic weights, but render bold from a real bold face rather than repeated glyphs. */
    public static Style resolveWeight(Style style) {
        if (!style.isBold() || !(style.getFont() instanceof FontDescription.Resource font)
            || !(font.id().equals(Identifier.withDefaultNamespace("default"))
                || font.id().equals(Identifier.withDefaultNamespace("uniform")))) return style;
        Minecraft minecraft = Minecraft.getInstance();
        return minecraft != null && minecraft.gui != null && applies(minecraft.gui.screen())
            ? style.withFont(BOLD).withBold(false) : style;
    }
}
