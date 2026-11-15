package dev.richasy.worldreroll;

import java.util.List;
import java.util.function.BooleanSupplier;
import net.minecraft.client.Minecraft;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.components.Button;
import net.minecraft.network.chat.Component;
import net.minecraft.util.FormattedCharSequence;
import com.mojang.blaze3d.platform.InputConstants;

/** Standalone world-reroll presentation using its own saved palette and native Minecraft font. */
final class RerollLook {
    int ink, muted, surface, edge, selection, hover, focus, primary, primaryInk;
    int heading, body, detail, control, gap, inset;

    RerollLook() { refresh(); }

    void refresh() {
        var window = Minecraft.getInstance().getWindow();
        boolean compact = window.getGuiScaledWidth() < 550 || window.getGuiScaledHeight() < 320;
        // Native pixel glyphs must remain at least their original height; all roles retain native readable text.
        heading = compact ? 12 : 15; body = 9; detail = 9;
        control = compact ? 20 : 24; gap = compact ? 6 : 8; inset = compact ? 12 : 16;
        boolean dark = RerollClientConfig.dark();
        ink = dark ? 0xFFF0F1E6 : 0xFF30291E; muted = dark ? 0xFFC0CCC3 : 0xFF574A34;
        surface = dark ? 0xE8182225 : 0xEEFFFCEF; edge = dark ? 0xA06E827F : 0xA09B722F;
        selection = dark ? 0xFF283639 : 0xFFE7C991; hover = dark ? 0xFF344346 : 0xFFFFF4D9; focus = dark ? 0xFFF4DE9B : 0xFF8B581D;
        primary = dark ? 0xFF76AC47 : 0xFF5B8730; primaryInk = 0xFFFFFFF0;
    }

    Component regular(Component text) { return text; }
    Component emphasized(Component text) { return text.copy().withStyle(style -> style.withBold(true)); }
    void panel(GuiGraphicsExtractor graphics, int x, int y, int width, int height) {
        graphics.fill(x + 1, y + 2, x + width + 1, y + height + 2, 0x22040C0F);
        graphics.fill(x, y, x + width, y + height, surface); graphics.outline(x, y, width, height, edge);
    }
    int height(Font font, Component text, int width, int size) {
        return lines(font, regular(text), width, size).size() * (size + 3);
    }
    int text(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int width, int size, int color) {
        return drawText(graphics, font, regular(text), x, y, width, size, color);
    }
    int emphasizedText(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int width, int size, int color) {
        return drawText(graphics, font, emphasized(text), x, y, width, size, color);
    }
    private int drawText(GuiGraphicsExtractor graphics, Font font, Component text, int x, int y, int width, int size, int color) {
        List<FormattedCharSequence> lines = lines(font, text, width, size);
        graphics.pose().pushMatrix();
        try {
            graphics.pose().translate(x, y); graphics.pose().scale(size / (float) font.lineHeight);
            for (int i = 0; i < lines.size(); i++) graphics.text(font, lines.get(i), 0, Math.round(i * (size + 3) * font.lineHeight / (float) size), color, false);
        } finally { graphics.pose().popMatrix(); }
        return lines.size() * (size + 3);
    }
    private List<FormattedCharSequence> lines(Font font, Component text, int width, int size) {
        return font.split(text, Math.max(1, Math.round(width * font.lineHeight / (float) size)));
    }
    private int choiceTextWidth(int width) {
        return width - gap * 5 - Math.max(7, body);
    }
    private int choiceDescriptionOffset(Font font, Component title, int width) {
        return lines(font, emphasized(title), width, body).size() * (body + 3) - 3 + Math.max(2, gap / 4);
    }
    int choiceHeight(Font font, Component title, Component hint, int width) {
        int textWidth = choiceTextWidth(width);
        return gap * 2 + choiceDescriptionOffset(font, title, textWidth) + height(font, hint, textWidth, detail) - 3;
    }
    Action button(int x, int y, int width, String key, boolean accent, Runnable action) {
        return new Action(x, y, width, control, Component.translatable(key), accent, action);
    }
    Choice choice(int x, int y, int width, int height, String key, Component hint, BooleanSupplier selected, Runnable action) {
        return new Choice(x, y, width, height, Component.translatable(key), hint, selected, action);
    }
    void scrollbar(GuiGraphicsExtractor graphics, int x, int y, int height, int total, int scroll) {
        int thumb = Math.max(8, height * height / Math.max(1, total));
        int offset = (height - thumb) * scroll / Math.max(1, total - height);
        graphics.fill(x, y, x + 2, y + height, edge);
        graphics.fill(x, y + offset, x + 2, y + offset + thumb, muted);
    }
    final class Action extends Button {
        private final boolean accent;
        Action(int x, int y, int width, int height, Component text, boolean accent, Runnable action) {
            super(x, y, width, height, text, button -> action.run(), DEFAULT_NARRATION); this.accent = accent;
        }
        @Override protected void extractContents(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
            var client = Minecraft.getInstance();
            boolean keyboard = client.getLastInputType().isKeyboard();
            boolean highlighted = isHovered() || isFocused() && keyboard;
            boolean down = highlighted && (isHovered() && client.mouseHandler.isLeftPressed()
                || isFocused() && (InputConstants.isKeyDown(InputConstants.KEY_RETURN) || InputConstants.isKeyDown(InputConstants.KEY_SPACE)));
            int color = !active ? muted : accent ? primaryInk : ink;
            graphics.fill(getX(), getY(), getRight(), getBottom(), !active ? 0xFF858984 : accent ? primary : down ? selection : highlighted ? hover : surface);
            graphics.outline(getX(), getY(), getWidth(), getHeight(), edge);
            Component label = regular(getMessage());
            float scale = body / (float) client.font.lineHeight;
            graphics.pose().pushMatrix();
            try {
                graphics.pose().translate(getX() + getWidth() / 2f, getY() + (getHeight() - body) / 2f); graphics.pose().scale(scale);
                graphics.text(client.font, label, -client.font.width(label) / 2, 0, color, false);
            } finally { graphics.pose().popMatrix(); }
            if (isFocused() && keyboard) graphics.outline(getX() - 1, getY() - 1, getWidth() + 2, getHeight() + 2, focus);
        }
    }

    /** One native focus target for the full option, with a neutral selection separate from the continue action. */
    final class Choice extends Button {
        private final Component hint;
        private final BooleanSupplier selected;
        Choice(int x, int y, int width, int height, Component title, Component hint, BooleanSupplier selected, Runnable action) {
            super(x, y, width, height, title, button -> action.run(), DEFAULT_NARRATION);
            this.hint = hint; this.selected = selected;
        }
        @Override protected void extractContents(GuiGraphicsExtractor graphics, int mouseX, int mouseY, float tick) {
            var client = Minecraft.getInstance();
            boolean keyboard = client.getLastInputType().isKeyboard();
            boolean chosen = selected.getAsBoolean();
            panel(graphics, getX(), getY(), getWidth(), getHeight());
            if (active && (chosen || isHovered()))
                graphics.fill(getX() + 2, getY() + 2, getRight() - 2, getBottom() - 2, chosen ? selection : hover);
            int radio = Math.max(7, body);
            int radioX = getX() + gap * 2, titleY = getY() + gap;
            int radioY = titleY + Math.max(0, (body - radio) / 2);
            graphics.outline(radioX, radioY, radio, radio, active ? ink : muted);
            if (chosen) graphics.fill(radioX + 2, radioY + 2, radioX + radio - 2, radioY + radio - 2, active ? ink : muted);
            int textX = radioX + radio + gap, available = getRight() - textX - gap * 2;
            emphasizedText(graphics, client.font, getMessage(), textX, titleY, available, body, active ? ink : muted);
            text(graphics, client.font, hint, textX, titleY + choiceDescriptionOffset(client.font, getMessage(), available), available, detail, muted);
            if (isFocused() && keyboard) graphics.outline(getX() - 1, getY() - 1, getWidth() + 2, getHeight() + 2, focus);
        }
    }

}
