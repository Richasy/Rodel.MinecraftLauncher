package io.github.richasy.rodel.ui.compat;

import com.terraformersmc.modmenu.config.ModMenuConfig;
import com.terraformersmc.modmenu.gui.widget.ModListWidget;
import com.terraformersmc.modmenu.gui.widget.UpdateAvailableBadge;
import com.terraformersmc.modmenu.gui.widget.entries.ModListEntry;
import com.terraformersmc.modmenu.gui.widget.entries.ParentEntry;
import com.terraformersmc.modmenu.util.DrawingUtil;
import com.terraformersmc.modmenu.util.mod.ModBadgeRenderer;
import com.terraformersmc.modmenu.util.mod.ModSearch;
import io.github.richasy.rodel.ui.MenuDensity;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import java.util.List;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.input.MouseButtonEvent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;

/** GUI-unit layout shared by paint, selection and input; the native entry owns all actions. */
public record ModBrowserRow(int left, int top, int right, int height, int iconX, int iconY, int iconSize,
                            int textX, int textWidth, int titleY, int titleHeight, int summaryY,
                            Component name, FormattedCharSequence shownName, int nameWidth,
                            boolean nameTrimmed, List<FormattedCharSequence> summary, boolean summaryTrimmed,
                            boolean update) {
    public interface Child {
        boolean rodel$isBottomChild();
    }

    public static int iconSize(MenuDensity density) {
        return Math.min(ModMenuConfig.COMPACT_LIST.getValue() ? 19 : 32, density.icon());
    }

    public static int rowHeight(MenuDensity density) {
        return Math.max(density.row(), iconSize(density) + density.inset() * 2);
    }

    public static ModBrowserRow measure(ModListEntry entry, ModernModsScreen browser) {
        Font font = Minecraft.getInstance().font;
        MenuDensity density = browser.rodel$modernContext().density();
        int icon = iconSize(density);
        int iconX = entry.getX() + entry.getXOffset();
        int right = entry.getX() + entry.getWidth();
        int textX = iconX + icon + density.gap();
        int textWidth = Math.max(1, right - textX);
        var mod = entry.getMod();
        boolean update = ModMenuConfig.UPDATE_CHECKER.getValue()
            && !ModMenuConfig.DISABLE_UPDATE_CHECKER.getValue().contains(mod.getId())
            && (mod.hasUpdate() || mod.getChildHasUpdate());
        Component name = Component.literal(mod.getTranslatedName());
        int nameBudget = Math.max(1, textWidth - (update ? 13 : 0));
        float bodyScale = density.body() / (float) font.lineHeight;
        boolean nameTrimmed = font.width(name) * bodyScale > nameBudget;
        var shownName = fit(name.getVisualOrderText(), font, nameBudget, density.body(), nameTrimmed);
        int nameWidth = (int) Math.ceil(font.width(shownName) * bodyScale);
        String summary = ModMenuConfig.COMPACT_LIST.getValue() ? mod.getPrefixedVersion() : mod.getSummary();
        var lines = summary == null || summary.isBlank() ? List.<FormattedCharSequence>of()
            : font.split(MenuText.secondary(Component.literal(summary.stripTrailing())),
                Math.max(1, (int) Math.floor(textWidth * font.lineHeight / (float) density.detail())));
        int lineCount = Math.min(2, lines.size());
        int titleHeight = Math.max(density.body(), density.detail() + 2);
        if (update) titleHeight = Math.max(titleHeight, 9);
        int textHeight = titleHeight + (lineCount == 0 ? 0 : 2 + lineCount * (density.detail() + 1) - 1);
        int titleY = entry.getY() + (entry.getHeight() - textHeight) / 2;
        return new ModBrowserRow(iconX - density.inset(), entry.getY(), right + density.inset(), entry.getHeight(),
            iconX, entry.getY() + (entry.getHeight() - icon) / 2, icon, textX, textWidth,
            titleY, titleHeight, titleY + titleHeight + 2, name, shownName, nameWidth,
            nameTrimmed, lines.subList(0, lineCount), lines.size() > lineCount, update);
    }

    public boolean contains(double x, double y) {
        return x >= left && x < right && y >= top && y < top + height;
    }

    public boolean iconContains(double x, double y) {
        return x >= iconX && x < iconX + iconSize && y >= iconY && y < iconY + iconSize;
    }

    public double nativeClickX(MouseButtonEvent event, ModListWidget list) {
        return list.getRowLeft() + (iconContains(event.x(), event.y()) ? 0 : iconSize + 1);
    }

    public void draw(GuiGraphicsExtractor graphics, ModListEntry entry, ModernModsScreen browser,
                     int mouseX, int mouseY, boolean hovered) {
        var theme = browser.theme();
        var density = browser.rodel$modernContext().density();
        var font = Minecraft.getInstance().font;
        var mod = entry.getMod();
        if (entry instanceof Child child) {
            int connectorX = entry.getX();
            int connectorY = iconY + iconSize / 2;
            int connectorRight = Math.max(connectorX + 1, iconX - density.gap());
            graphics.fill(connectorX, top, connectorX + 1,
                child.rodel$isBottomChild() ? connectorY + 1 : top + height, theme.edge());
            graphics.fill(connectorX, connectorY, connectorRight, connectorY + 1, theme.edge());
        }
        if (mod.getId().equals("java")) DrawingUtil.drawRandomVersionBackground(mod, graphics, iconX, iconY, iconSize, iconSize);
        graphics.blit(RenderPipelines.GUI_TEXTURED, entry.getIconTexture(), iconX, iconY, 0, 0,
            iconSize, iconSize, iconSize, iconSize, -1);
        drawText(graphics, font, ModBrowserText.themed(shownName, theme), textX,
            titleY + (titleHeight - density.body()) / 2f, density.body(), theme.ink());
        int nextX = textX + nameWidth + 3;
        if (update) {
            UpdateAvailableBadge.renderBadge(graphics, nextX, titleY + (titleHeight - 9) / 2);
            nextX += 13;
        }
        if (!ModMenuConfig.HIDE_BADGES.getValue()) {
            new ModBadgeRenderer(nextX, titleY + (titleHeight - density.detail() - 2) / 2,
                textX + textWidth, mod, browser).draw(graphics, mouseX, mouseY);
        }
        for (int i = 0; i < summary.size(); i++) {
            var line = fit(summary.get(i), font, textWidth, density.detail(), summaryTrimmed && i == summary.size() - 1);
            drawText(graphics, font, line, textX, summaryY + i * (density.detail() + 1), density.detail(), theme.muted());
        }
        if (nameTrimmed && hovered && mouseX >= textX && mouseX < textX + textWidth
            && mouseY >= titleY && mouseY < titleY + titleHeight) graphics.setTooltipForNextFrame(name, mouseX, mouseY);
        if (entry instanceof ParentEntry parent) {
            int visible = ModSearch.search(browser, browser.getSearchInput(), parent.getChildren()).size();
            Component count = MenuText.secondary(Component.literal(visible == parent.getChildren().size()
                ? Integer.toString(visible) : visible + "/" + parent.getChildren().size()));
            int countWidth = (int) Math.ceil(font.width(count) * density.detail() / (float) font.lineHeight) + 4;
            graphics.fill(iconX + iconSize - countWidth, iconY + iconSize - density.detail() - 2,
                iconX + iconSize, iconY + iconSize, theme.surface());
            MenuText.extract(graphics, font, count, iconX + iconSize - countWidth + 2,
                iconY + iconSize - density.detail() - 1, density.detail(), theme.ink());
            if (hovered) {
                graphics.fill(iconX, iconY, iconX + iconSize, iconY + iconSize, 0x99000000);
                graphics.pose().pushMatrix();
                graphics.pose().translate(iconX + iconSize / 2f, iconY + iconSize / 2f);
                if (browser.showModChildren.contains(mod.getId())) graphics.pose().rotate((float) (Math.PI / 2));
                MenuIcon.ARROW.extract(graphics, -iconSize / 3f, -iconSize / 3f, iconSize * 2f / 3, 0xFFFFFFFF);
                graphics.pose().popMatrix();
            }
        } else if (hovered && ModMenuConfig.QUICK_CONFIGURE.getValue()
            && (browser.getModHasConfigScreen(mod.getId()) || browser.modScreenErrors.containsKey(mod.getId()))) {
            graphics.fill(iconX, iconY, iconX + iconSize, iconY + iconSize, 0x99000000);
            (browser.modScreenErrors.containsKey(mod.getId()) ? MenuIcon.BUG : MenuIcon.SETTINGS).extract(
                graphics, iconX + 3, iconY + 3, iconSize - 6, 0xFFFFFFFF);
        }
        if (hovered && iconContains(mouseX, mouseY)
            && (entry instanceof ParentEntry || ModMenuConfig.QUICK_CONFIGURE.getValue()
                && browser.getModHasConfigScreen(mod.getId()))) graphics.requestCursor(entry.shouldTakeFocusAfterInteraction()
                    ? com.mojang.blaze3d.platform.cursor.CursorTypes.POINTING_HAND
                    : com.mojang.blaze3d.platform.cursor.CursorTypes.NOT_ALLOWED);
    }

    private static FormattedCharSequence fit(FormattedCharSequence text, Font font, int width, int size, boolean ellipsize) {
        if (!ellipsize) return text;
        var dots = MenuText.secondary(Component.literal("…")).getVisualOrderText();
        int available = Math.max(0, (int) Math.floor(width * font.lineHeight / (float) size) - font.width(dots));
        return FormattedCharSequence.composite(ModBrowserText.fitVisual(text, font, available), dots);
    }

    private static void drawText(GuiGraphicsExtractor graphics, Font font, FormattedCharSequence text,
                                 float x, float y, int size, int color) {
        graphics.pose().pushMatrix();
        graphics.pose().translate(x, y);
        graphics.pose().scale(size / (float) font.lineHeight);
        graphics.text(font, text, 0, 0, color, false);
        graphics.pose().popMatrix();
    }
}
