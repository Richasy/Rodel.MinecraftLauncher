package io.github.richasy.bettertools.client;

import io.github.richasy.bettertools.CubeTooltip;
import io.github.richasy.bettertools.CubeState;
import io.github.richasy.bettertools.ToolKind;
import java.util.List;
import net.minecraft.client.gui.Font;
import net.minecraft.client.gui.GuiGraphicsExtractor;
import net.minecraft.client.gui.screens.inventory.tooltip.ClientTooltipComponent;
import net.minecraft.client.renderer.RenderPipelines;
import net.minecraft.core.NonNullList;
import net.minecraft.network.chat.Component;
import net.minecraft.network.chat.MutableComponent;
import net.minecraft.util.FormattedCharSequence;
import net.minecraft.world.item.ItemStack;

/** The approved compact cube card and selected-original bubble share vanilla's tooltip positioning and GUI scale. */
public final class ClientCubeTooltip implements ClientTooltipComponent {
    private static final int CELL = 24;
    private static final int STEP = 26;
    private static final int HEADER = 20;
    private static final int FOOTER = 18;
    private static final int GRID_HEIGHT = STEP + CELL;
    private final CubeTooltip preview;
    private final NonNullList<ItemStack> items = NonNullList.withSize(CubeState.SHELL_SLOT + 1, ItemStack.EMPTY);

    /** Retains a detached synchronized snapshot; rendering never edits the real cube or its original tools. */
    public ClientCubeTooltip(CubeTooltip preview) {
        this.preview = preview;
        preview.contents().copyInto(items);
    }

    @Override public int getHeight(Font font) { return HEADER + GRID_HEIGHT + FOOTER + 8 + detailHeight(font); }
    @Override public int getWidth(Font font) {
        return Math.max(192, Math.max(font.width(title()) + 48,
                Math.max(labelWidth(font) + 8 + CubeState.COLUMNS * STEP - 2 + 12, controlsWidth(font) + 12)));
    }
    @Override public boolean showTooltipWithItemInHand() { return true; }

    @Override public void extractImage(Font font, int x, int y, int width, int height, GuiGraphicsExtractor graphics) {
        var look = CubeTooltipLook.current();
        int cardWidth = getWidth(font), cardY = y + detailHeight(font);
        int cardHeight = HEADER + GRID_HEIGHT + FOOTER + 8;
        int gridX = x + 6 + labelWidth(font) + 8, gridY = cardY + HEADER + 2;
        look.panel(graphics, x, cardY, cardWidth, cardHeight);
        CubeTooltipIcons.cube(graphics, x + 6, cardY + 5, look.ink());
        var title = title();
        graphics.text(font, look.text(title), x + 21, cardY + 6, look.ink(), false);
        look.rule(graphics, x + 25 + font.width(title), cardY + 10, x + cardWidth - 21);
        CubeTooltipIcons.info(graphics, x + cardWidth - 15, cardY + 5, look.muted());
        for (int row = 0; row < 2; row++) {
            int rowY = gridY + row * STEP;
            graphics.text(font, look.text(row(row).withStyle(style -> style.withBold(true))), x + 6, rowY + 5, look.ink(), false);
            small(graphics, font, Component.translatable(row == 0 ? "bettertools.row.primary.detail" : "bettertools.row.reserve.detail"),
                    x + 6, rowY + 15, look.muted());
            for (int col = 0; col < CubeState.COLUMNS; col++) {
                int i = row * CubeState.COLUMNS + col, sx = gridX + col * STEP;
                boolean picked = preview.picked() == i;
                look.slot(graphics, sx, rowY, picked);
                var item = items.get(i);
                if (item.isEmpty()) graphics.blit(RenderPipelines.GUI_TEXTURED, ToolOutlineTexture.id(ToolKind.values()[col]),
                        sx + 4, rowY + 4, 0, 0, 16, 16, 64, 64, 64, 64, look.muted());
                else {
                    graphics.item(item, sx + 4, rowY + 4, i);
                    graphics.itemDecorations(font, item, sx + 4, rowY + 4);
                }
            }
        }
        int footerY = gridY + GRID_HEIGHT + 3;
        look.rule(graphics, x + 1, footerY, x + cardWidth - 1);
        controls(graphics, font, x + (cardWidth - controlsWidth(font)) / 2, footerY + 5, look);
        if (!selected().isEmpty()) detail(graphics, font, x, y, cardWidth, gridX, look);
    }

    private void controls(GuiGraphicsExtractor graphics, Font font, int x, int y, CubeTooltipLook look) {
        boolean empty = items.stream().allMatch(ItemStack::isEmpty);
        CubeTooltipIcons.mouse(graphics, x, y - 1, look.ink(), look.accent());
        var operation = Component.translatable(empty ? "bettertools.hint.insert" : "bettertools.hint.actions");
        small(graphics, font, operation, x + 13, y + 1, look.ink());
        if (empty) return;
        int next = x + 13 + smallWidth(font, operation) + 6;
        graphics.fill(next, y, next + 1, y + 9, look.muted());
        next += 7;
        look.keycap(graphics, next, y - 1, 17, 11);
        var tab = Component.literal("Tab");
        small(graphics, font, tab, next + (17 - smallWidth(font, tab)) / 2, y + 1, look.ink());
        small(graphics, font, Component.translatable("bettertools.hint.priority"), next + 20, y + 1, look.ink());
    }

    private void detail(GuiGraphicsExtractor graphics, Font font, int x, int y, int width, int gridX, CubeTooltipLook look) {
        int bubbleWidth = bubbleWidth(font);
        int left = Math.max(x + 6, Math.min(gridX + preview.picked() % CubeState.COLUMNS * STEP + 30, x + width - bubbleWidth - 26));
        int bubbleHeight = bubbleHeight(font);
        look.detailPanel(graphics, left, y, bubbleWidth, bubbleHeight);
        int textY = y + 4;
        for (var line : detailLines(font, bubbleWidth - 8)) {
            graphics.text(font, line, left + 4, textY, 0xFFF2EFEA, false);
            textY += font.lineHeight + 1;
        }
        if (selected().isDamageableItem()) {
            graphics.text(font, durability(), left + 4, textY, 0xFFBEBBC2, false);
            textY += font.lineHeight + 1;
        }
        graphics.text(font, role(), left + 4, textY, 0xFFBEBBC2, false);
    }

    private List<FormattedCharSequence> detailLines(Font font, int width) { return font.split(selected().getHoverName(), width); }
    private int bubbleWidth(Font font) {
        return Math.max(82, Math.min(getWidth(font) - 32, Math.max(font.width(selected().getHoverName()),
                Math.max(font.width(durability()), font.width(role()))) + 8));
    }
    private int bubbleHeight(Font font) {
        return 8 + (detailLines(font, bubbleWidth(font) - 8).size() + (selected().isDamageableItem() ? 2 : 1)) * (font.lineHeight + 1);
    }
    private int detailHeight(Font font) {
        if (selected().isEmpty()) return 0;
        return Math.max(0, bubbleHeight(font) - HEADER - 2);
    }
    private ItemStack selected() { return preview.picked() < 0 ? ItemStack.EMPTY : items.get(preview.picked()); }
    private MutableComponent title() { return preview.title().copy().withStyle(style -> style.withBold(true)); }
    private Component durability() {
        var tool = selected();
        return Component.translatable("bettertools.durability", Component.literal(Integer.toString(Math.max(0, tool.getMaxDamage() - tool.getDamageValue())))
                .withColor(0x45DB32), tool.getMaxDamage());
    }
    private Component role() { return Component.translatable("bettertools.detail.role", row(preview.picked() / CubeState.COLUMNS)); }
    private int controlsWidth(Font font) {
        boolean empty = items.stream().allMatch(ItemStack::isEmpty);
        int width = 13 + smallWidth(font, Component.translatable(empty ? "bettertools.hint.insert" : "bettertools.hint.actions"));
        return empty ? width : width + 6 + 1 + 7 + 20 + smallWidth(font, Component.translatable("bettertools.hint.priority"));
    }
    private static int smallWidth(Font font, Component message) { return font.width(message); }
    private static void small(GuiGraphicsExtractor graphics, Font font, Component message, int x, int y, int ink) {
        // Native bitmap glyphs must stay on the native GUI pixel grid; fractional downscaling drops CJK strokes.
        graphics.text(font, CubeTooltipLook.current().text(message, ink), x, y, ink, false);
    }
    private static MutableComponent row(int index) { return Component.translatable(index == 0 ? "bettertools.row.primary" : "bettertools.row.reserve"); }
    private static int labelWidth(Font font) {
        return Math.max(Math.max(font.width(row(0).withStyle(style -> style.withBold(true))), font.width(row(1).withStyle(style -> style.withBold(true)))),
                Math.max(smallWidth(font, Component.translatable("bettertools.row.primary.detail")),
                        smallWidth(font, Component.translatable("bettertools.row.reserve.detail"))));
    }
}
