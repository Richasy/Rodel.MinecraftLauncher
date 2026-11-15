package io.github.richasy.rodel.ui.compat;

import java.util.List;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractSelectionList;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.FormattedText;
import net.minecraft.util.FormattedCharSequence;

/** Geometry shared by the retained Mod Menu metadata rows and their link hit areas. */
public final class ModBrowserDetails {
    // Mod Menu 21 draws this exact suffix after a contact name.
    private static final Component CONTACT_SUFFIX = Component.literal(" \u2709");
    public static final int NATIVE_UPDATE_ICON_SIZE = 8;

    private ModBrowserDetails() {}

    public interface EntryText {
        FormattedCharSequence rodel$detailText();
    }

    public interface Contact {}

    public static int textSize(ModernModsScreen browser) {
        return browser.rodel$modernContext().density().detail();
    }

    public static int rowHeight(ModernModsScreen browser) {
        return textSize(browser) + 3;
    }

    public static float textScale(ModernModsScreen browser, Font font) {
        return textSize(browser) / (float) font.lineHeight;
    }

    public static int textWidth(ModernModsScreen browser, Font font, FormattedCharSequence text) {
        return (int) Math.ceil(font.width(ModBrowserText.secondary(text)) * textScale(browser, font));
    }

    public static int textY(ModernModsScreen browser, AbstractSelectionList.Entry<?> entry) {
        return entry.getY() + Math.max(0, (entry.getHeight() - textSize(browser)) / 2);
    }

    public static int updateIconSize(ModernModsScreen browser) {
        return textSize(browser) + 1;
    }

    public static int updateAdvance(ModernModsScreen browser) {
        return updateIconSize(browser) + 2;
    }

    public static int contactAdvance(ModernModsScreen browser, Font font) {
        return 1 + textWidth(browser, font, CONTACT_SUFFIX.getVisualOrderText());
    }

    public static List<FormattedCharSequence> split(ModernModsScreen browser, Font font, FormattedText text,
                                                   int availableWidth) {
        int width = Math.max(1, (int) Math.floor(availableWidth / textScale(browser, font)));
        return font.split(ModBrowserText.secondary(text), width);
    }

    public static void text(ModernModsScreen browser, GuiGraphicsExtractor graphics, Font font,
                            FormattedCharSequence text, int x, int y, int color) {
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x, y);
            graphics.pose().scale(textScale(browser, font));
            graphics.text(font, ModBrowserText.themed(ModBrowserText.secondary(text), browser.theme()),
                0, 0, color, false);
        } finally {
            graphics.pose().popMatrix();
        }
    }
}
