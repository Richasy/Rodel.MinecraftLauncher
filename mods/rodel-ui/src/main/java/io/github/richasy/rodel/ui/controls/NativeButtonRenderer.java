package io.github.richasy.rodel.ui.controls;

import com.mojang.blaze3d.platform.InputConstants;
import io.github.richasy.rodel.ui.MenuContext;
import io.github.richasy.rodel.ui.MenuIcon;
import io.github.richasy.rodel.ui.MenuText;
import io.github.richasy.rodel.ui.MenuTheme;
import io.github.richasy.rodel.ui.TitleMenu;
import io.github.richasy.rodel.ui.menus.MenuButtonIcon;
import io.github.richasy.rodel.ui.menus.NativeMenuPage;
import io.github.richasy.rodel.ui.settings.NativeFormContext;
import io.github.richasy.rodel.ui.worlds.WorldPage;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.AbstractWidget;
import net.minecraft.client.gui.components.CycleButton;
import net.minecraft.network.chat.Component;

/** Draws an already-adapted native button; its owner retains geometry, input, tooltips and callbacks. */
public final class NativeButtonRenderer {
    private NativeButtonRenderer() {}

    /** Extracts the current button surface and content without changing the native widget or its value. */
    public static void draw(GuiGraphicsExtractor graphics, AbstractWidget button, MenuContext menu,
                            MenuIcon icon, boolean primary, boolean iconOnly, boolean side) {
        Minecraft minecraft = Minecraft.getInstance();
        MenuTheme theme = menu.theme();
        float menuScale = menu.scale();
        int width = Math.round(button.getWidth() / menuScale);
        int height = Math.round(button.getHeight() / menuScale);
        boolean highlighted = button.isActive() && (button.isHovered() || button.isFocused() && menu.focusVisible());
        boolean pressed = highlighted && (button.isHovered() && minecraft.mouseHandler.isLeftPressed()
            || button.isFocused() && (InputConstants.isKeyDown(InputConstants.KEY_RETURN)
                || InputConstants.isKeyDown(InputConstants.KEY_NUMPADENTER)
                || InputConstants.isKeyDown(InputConstants.KEY_SPACE)));
        Boolean switchValue = menu instanceof NativeFormContext && button instanceof CycleButton<?> cycle
            && cycle.getValue() instanceof Boolean value ? value : null;
        boolean combo = menu instanceof NativeFormContext && button instanceof CycleButton<?> && switchValue == null;
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(button.getX(), button.getY());
            graphics.pose().scale(menuScale);
            int ink = switchValue == null ? theme.button(graphics, width, height, primary, highlighted, pressed, button.isActive()) : theme.ink();
            if (switchValue != null) drawSwitch(graphics, button, (NativeFormContext) menu, width, height, switchValue, highlighted, pressed);
            if (!button.isActive()) ink = theme.muted();
            if (button.isFocused() && menu.focusVisible()) {
                graphics.outline(-3, -3, width + 6, height + 6, theme.focus());
                graphics.outline(-4, -4, width + 8, height + 8, theme.focus());
            }
            boolean denseTitle = menu instanceof TitleMenu title && title.dense();
            boolean densePage = !(menu instanceof TitleMenu) && menu.density().tight();
            boolean nativeSprite = menu instanceof NativeMenuPage && button instanceof MenuButtonIcon;
            float iconSize = denseTitle || densePage ? iconOnly ? 14 : 12 : iconOnly ? 22 : nativeSprite ? 14 : primary ? 18 : 20;
            if (icon == null) drawValue(graphics, button, menu, width, height, switchValue, combo, denseTitle || densePage, ink);
            else if (iconOnly) icon.extract(graphics, (width - iconSize) / 2, (height - iconSize) / 2, iconSize, ink);
            else drawLabel(graphics, button, menu, icon, side, width, height, iconSize, denseTitle, densePage, nativeSprite, ink);
        } finally {
            graphics.pose().popMatrix();
        }
    }

    private static void drawSwitch(GuiGraphicsExtractor graphics, AbstractWidget button, NativeFormContext page,
                                   int width, int height, boolean value, boolean highlighted, boolean pressed) {
        var font = Minecraft.getInstance().font;
        Component state = MenuText.styled(page.controlText(button));
        float stateWidth = font.width(state) * page.controlTextSize() / (float) font.lineHeight;
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(width - stateWidth - 64, 0);
            page.theme().switchTrack(graphics, (height - 28) / 2, value, highlighted, pressed, button.isActive());
        } finally {
            graphics.pose().popMatrix();
        }
    }

    private static void drawValue(GuiGraphicsExtractor graphics, AbstractWidget button, MenuContext menu,
                                  int width, int height, Boolean switchValue, boolean combo, boolean dense, int ink) {
        var font = Minecraft.getInstance().font;
        Component label = MenuText.styled(menu instanceof NativeFormContext page ? page.controlText(button) : button.getMessage());
        float textScale = (menu instanceof NativeFormContext page ? page.controlTextSize() : dense ? 12f : 18f) / font.lineHeight;
        if (menu instanceof NativeFormContext page)
            label = page.fitValue(button, label, (width - (switchValue != null ? 64 : combo ? 48 : 28)) / textScale);
        MenuText.extract(graphics, font, label,
            switchValue != null ? width - font.width(label) * textScale : combo ? 14 : (width - font.width(label) * textScale) / 2,
            (height - font.lineHeight * textScale) / 2, font.lineHeight * textScale, ink);
        if (combo) drawComboArrow(graphics, width, height, ink);
    }

    private static void drawComboArrow(GuiGraphicsExtractor graphics, int width, int height, int ink) {
        for (int i = 0; i < 4; i++) {
            graphics.fill(width - 26 + i, height / 2 - 2 + i, width - 24 + i, height / 2 - 1 + i, ink);
            graphics.fill(width - 18 - i, height / 2 - 2 + i, width - 16 - i, height / 2 - 1 + i, ink);
        }
    }

    private static void drawLabel(GuiGraphicsExtractor graphics, AbstractWidget button, MenuContext menu, MenuIcon icon,
                                  boolean side, int width, int height, float iconSize, boolean denseTitle,
                                  boolean densePage, boolean nativeSprite, int ink) {
        var font = Minecraft.getInstance().font;
        Component label = MenuText.styled(menu instanceof WorldPage page ? page.controlText(button) : button.getMessage());
        float fontSize = denseTitle ? side ? 10 : 12 : densePage ? 12
            : side ? menu.compact() ? 13 : 15 : menu.compact() ? 16 : 18;
        float textScale = fontSize / font.lineHeight;
        int gap = nativeSprite || denseTitle || densePage ? 8 : 12;
        int padding = nativeSprite || denseTitle || densePage ? 12 : 18;
        int arrowSize = denseTitle || densePage ? 10 : 14;
        int arrowGap = denseTitle || densePage ? 8 : 12;
        float availableText = width - 2 * padding - iconSize - gap - (side ? arrowGap + arrowSize : 0);
        if (menu instanceof NativeFormContext page) label = page.fitValue(button, label, availableText / textScale);
        else textScale = Math.min(textScale, availableText / Math.max(1, font.width(label)));
        float labelWidth = font.width(label) * textScale;
        float contentWidth = iconSize + gap + labelWidth;
        float contentX = side ? denseTitle ? padding : menu.compact() ? 14 : 18 : (width - contentWidth) / 2;
        icon.extract(graphics, contentX, (height - iconSize) / 2, iconSize, ink);
        MenuText.extract(graphics, font, label, contentX + iconSize + gap,
            (height - font.lineHeight * textScale) / 2, font.lineHeight * textScale, ink);
        if (side) MenuIcon.ARROW.extract(graphics, width - (denseTitle ? padding : menu.compact() ? 14 : 18) - arrowSize,
            (height - arrowSize) / 2f, arrowSize, (ink & 0x00FFFFFF) | 0x99000000);
    }
}
